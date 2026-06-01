using System.Text.Json;
using RunFence.Launching.Processes;

namespace RunFence.AppxLauncher;

public static class Program
{
    public static int Main(string[] args)
    {
        if (!AppxLauncherArgumentParser.TryParse(args, Environment.CommandLine, out var options, out var error))
        {
            var parseFailure = AppxLaunchResult.Failed(AppxLaunchExitCode.InvalidArguments, "ParseArguments", error);
            var logFilePath = TryGetLogFilePath(args);
            if (!string.IsNullOrWhiteSpace(logFilePath)
                && !TryWriteResultFile(logFilePath, null, null, parseFailure, out var writeError))
            {
                var transportFailure = AppxLaunchResult.Failed(
                    AppxLaunchExitCode.ResultFileWriteFailed,
                    "WriteResultFile",
                    $"Could not write parse failure result: {writeError}");
                WriteFailureResult(transportFailure, null, null);
                return (int)transportFailure.ExitCode;
            }

            WriteFailureResult(parseFailure, null, null);
            return (int)parseFailure.ExitCode;
        }

        return LaunchParsed(options.LogFilePath, options.AppxExecutablePath, options.Arguments);
    }

    public static int LaunchParsed(string logFilePath, string appxExecutablePath, string arguments)
    {
        var options = new AppxLauncherStartupOptions(logFilePath, appxExecutablePath, arguments);
        var staDispatcher = new WinRtStaDispatcher();
        var processScanner = new ProcessSnapshotScanner();
        IProcessImageNameSnapshotReader processImageNameReader = processScanner;
        IProcessExecutablePathReader processPathReader = processScanner;
        IProcessOwnerInfoReader processOwnerReader = processScanner;
        var packageManagerContextFactory = new WinRtPackageManagerContextFactory(staDispatcher);
        var launcher = new AppxLaunchOrchestrator(
            new AppxManifestLaunchMetadataResolver(),
            new DesktopAppxActivationLauncher(),
            new WinRtPackageRegistration(packageManagerContextFactory),
            new WinRtUriProtocolLauncher(staDispatcher),
            new ShellUriProtocolLauncher(),
            new AppxLaunchAttemptVerifier(
                new AppxTargetProcessQuery(processImageNameReader, processPathReader, processOwnerReader),
                new WindowsIdentityAppxCurrentUserSidProvider(),
                new SystemAppxLaunchVerificationClock()));
        var result = launcher.Launch(options);
        if (result.Success)
            return (int)result.ExitCode;

        if (!TryWriteResultFile(options.LogFilePath, options.AppxExecutablePath, options.Arguments, result, out var resultWriteError))
        {
            var transportFailure = AppxLaunchResult.Failed(
                AppxLaunchExitCode.ResultFileWriteFailed,
                "WriteResultFile",
                $"Could not write result for stage '{result.Stage}': {resultWriteError}");
            WriteFailureResult(transportFailure, options.AppxExecutablePath, options.Arguments);
            return (int)transportFailure.ExitCode;
        }

        WriteFailureResult(result, options.AppxExecutablePath, options.Arguments);
        return (int)result.ExitCode;
    }

    private static void WriteFailureResult(AppxLaunchResult result, string? appxExecutablePath, string? arguments)
    {
        Console.Error.WriteLine(SerializePayload(result, appxExecutablePath, arguments));
    }

    private static bool TryWriteResultFile(
        string? logFilePath,
        string? appxExecutablePath,
        string? arguments,
        AppxLaunchResult result,
        out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(logFilePath))
        {
            error = "Log file path is empty.";
            return false;
        }

        try
        {
            var directory = Path.GetDirectoryName(logFilePath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            var payload = SerializePayload(result, appxExecutablePath, arguments);
            var tempFilePath = Path.Combine(directory ?? AppContext.BaseDirectory, Path.GetRandomFileName());
            try
            {
                File.WriteAllText(tempFilePath, payload + Environment.NewLine);
                File.Move(tempFilePath, logFilePath, true);
            }
            finally
            {
                try
                {
                    if (File.Exists(tempFilePath))
                        File.Delete(tempFilePath);
                }
                catch
                {
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static string? TryGetLogFilePath(string[] args)
        => args.Length > 0 && !string.IsNullOrWhiteSpace(args[0]) ? args[0] : null;

    private static string SerializePayload(AppxLaunchResult result, string? appxExecutablePath, string? arguments) =>
        JsonSerializer.Serialize(new AppxLaunchResultPayload(
            result.Success,
            result.Stage,
            (int)result.ExitCode,
            result.HResult.HasValue ? $"0x{unchecked((uint)result.HResult.Value):X8}" : null,
            result.Message,
            appxExecutablePath,
            arguments));
}
