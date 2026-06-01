using System.Text;
using RunFence.Core;

namespace RunFence.JobKeeper;

public sealed class JobKeeperChildProcessLauncher(
    IJobKeeperExecutablePathResolver executablePathResolver,
    IJobKeeperEnvironmentSnapshotReader environmentSnapshotReader,
    IJobKeeperEnvironmentBlockFactory environmentBlockFactory,
    IJobKeeperNativeProcessApi nativeProcessApi,
    IJobKeeperChildProcessRegistry childProcessRegistry) : IJobKeeperChildProcessLauncher
{
    private const uint CreateUnicodeEnvironment = 0x00000400;
    private const int Win32ErrorElevationRequired = 740;
    private const string CompatLayerVariableName = "__COMPAT_LAYER";
    private const string RunAsInvokerCompatLayerValue = "RunAsInvoker";

    public JobKeeperLaunchResponse Launch(JobKeeperLaunchRequest request)
    {
        var environment = environmentSnapshotReader.ReadAll();
        if (request.EnvOverrides != null)
        {
            foreach (var (key, value) in request.EnvOverrides)
                environment[key] = value;
        }

        var exePath = executablePathResolver.Resolve(request.ExePath, environment);

        var cmdLine = new StringBuilder();
        cmdLine.Append('"').Append(exePath).Append('"');
        if (!string.IsNullOrEmpty(request.Arguments))
            cmdLine.Append(' ').Append(request.Arguments);

        using var envBlock = environmentBlockFactory.Build(environment);
        var applicationName = Path.IsPathFullyQualified(exePath) ? exePath : null;
        if (!TryCreateProcess(
                applicationName,
                cmdLine,
                envBlock.Pointer,
                request.WorkingDirectory,
                request.HideWindow,
                request.SuppressStartupFeedback,
                out var processInfo))
        {
            var error = nativeProcessApi.GetLastWin32Error();
            if (error == Win32ErrorElevationRequired
                && !environment.ContainsKey(CompatLayerVariableName))
            {
                var retryEnvironment = new Dictionary<string, string>(environment, StringComparer.OrdinalIgnoreCase)
                {
                    [CompatLayerVariableName] = RunAsInvokerCompatLayerValue
                };
                using var retryEnvBlock = environmentBlockFactory.Build(retryEnvironment);
                if (TryCreateProcess(
                        applicationName,
                        cmdLine,
                        retryEnvBlock.Pointer,
                        request.WorkingDirectory,
                        request.HideWindow,
                        request.SuppressStartupFeedback,
                        out processInfo))
                {
                    nativeProcessApi.CloseHandle(processInfo.ThreadHandle);
                    childProcessRegistry.Register(processInfo.ProcessHandle);
                    return new JobKeeperLaunchResponse((int)processInfo.ProcessId, 0, processInfo.ProcessHandle.ToInt64());
                }

                error = nativeProcessApi.GetLastWin32Error();
            }

            return new JobKeeperLaunchResponse(0, error);
        }

        nativeProcessApi.CloseHandle(processInfo.ThreadHandle);
        childProcessRegistry.Register(processInfo.ProcessHandle);
        return new JobKeeperLaunchResponse((int)processInfo.ProcessId, 0, processInfo.ProcessHandle.ToInt64());
    }

    private bool TryCreateProcess(
        string? applicationName,
        StringBuilder commandLine,
        IntPtr environmentBlock,
        string? workingDirectory,
        bool hideWindow,
        bool suppressStartupFeedback,
        out JobKeeperProcessInformation processInfo)
        => nativeProcessApi.CreateProcess(
            applicationName,
            new StringBuilder(commandLine.ToString()),
            CreateUnicodeEnvironment,
            environmentBlock,
            string.IsNullOrWhiteSpace(workingDirectory) ? null : workingDirectory,
            hideWindow,
            suppressStartupFeedback,
            out processInfo);
}
