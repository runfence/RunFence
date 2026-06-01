using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Security.Principal;
using System.Text;
using RunFence.AppxLauncher;
using RunFence.Core;
using Xunit;

namespace RunFence.IntegrationTests;

public sealed class AppxLaunchOrchestratorProbeTests
{
    private const string CodexProcessName = "Codex";
    private const string CodexUri = "codex:";
    private const string CodexDesktopActivationArguments = "";
    private static readonly TimeSpan ProcessAppearanceTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ProcessSurvivalCheckDelay = TimeSpan.FromSeconds(3);

    [Fact]
    public void Probe_CodexUriLaunch_ReturnsSuccess()
    {
        var packageInfo = QueryCodexPackageInfo();
        var executablePath = ResolveCodexExecutablePath(packageInfo);

        ProbeCodexLaunch(executablePath, () =>
        {
            var stopwatch = Stopwatch.StartNew();
            AppxLaunchResult result = default;
            RunOnSta(() =>
            {
                var staDispatcher = new WinRtStaDispatcher();
                var launcher = new WinRtUriProtocolLauncher(staDispatcher);
                result = launcher.Launch(new AppxUriLaunchOptions(packageInfo.PackageFamilyName, CodexUri));
            });
            stopwatch.Stop();
            return (result, stopwatch.Elapsed);
        });
    }

    [Fact]
    public void Probe_CodexDesktopAppxActivation_ReturnsSuccess()
    {
        var executablePath = ResolveCodexExecutablePath();

        ProbeCodexLaunch(executablePath, () =>
        {
            var metadataResult = new AppxManifestLaunchMetadataResolver().Resolve(
                executablePath,
                CodexDesktopActivationArguments);
            Assert.True(
                metadataResult.Success,
                $"exitCode={metadataResult.Error.ExitCode}; stage={metadataResult.Error.Stage}; " +
                $"hresult={metadataResult.Error.HResult}; message={metadataResult.Error.Message}");
            Assert.True(metadataResult.Metadata.IsFullTrustApplication);

            var stopwatch = Stopwatch.StartNew();
            var result = new DesktopAppxActivationLauncher().Launch(
                metadataResult.Metadata,
                CodexDesktopActivationArguments);
            stopwatch.Stop();
            return (result, stopwatch.Elapsed);
        });
    }

    private static void ProbeCodexLaunch(
        string expectedExecutablePath,
        Func<(AppxLaunchResult Result, TimeSpan Elapsed)> launch)
    {
        var currentUserSid = WindowsIdentity.GetCurrent().User?.Value
                             ?? throw new InvalidOperationException("Current user SID is unavailable.");
        var initialCodexPids = GetCurrentUserCodexProcessIds(currentUserSid, expectedExecutablePath);
        HashSet<int> launchedCodexPids = [];

        try
        {
            var (result, elapsed) = launch();

            Assert.True(
                result.Success,
                $"exitCode={result.ExitCode}; stage={result.Stage}; hresult={result.HResult}; " +
                $"message={result.Message}; elapsedMs={elapsed.TotalMilliseconds}");
            launchedCodexPids = WaitForNewCurrentUserCodexProcessIds(
                currentUserSid,
                expectedExecutablePath,
                initialCodexPids);
            Assert.NotEmpty(launchedCodexPids);

            Thread.Sleep(ProcessSurvivalCheckDelay);
            var survivingCodexPids = GetCurrentUserCodexProcessIds(currentUserSid, expectedExecutablePath);
            launchedCodexPids.ExceptWith(survivingCodexPids);
            Assert.Empty(launchedCodexPids);
        }
        finally
        {
            TerminateCurrentUserCodexProcessesExcept(currentUserSid, expectedExecutablePath, initialCodexPids);
        }
    }

    private static string ResolveCodexExecutablePath()
    {
        var packageInfo = QueryCodexPackageInfo();
        return ResolveCodexExecutablePath(packageInfo);
    }

    private static string ResolveCodexExecutablePath(CodexPackageInfo packageInfo)
    {
        var executablePath = Path.Combine(packageInfo.InstallLocation, "app", "Codex.exe");
        if (!File.Exists(executablePath))
            throw new FileNotFoundException("Codex executable was not found under the installed package location.", executablePath);

        return executablePath;
    }

    private static CodexPackageInfo QueryCodexPackageInfo()
    {
        const string script = """
            $ErrorActionPreference = 'Stop'
            $package = Get-AppxPackage -Name OpenAI.Codex
            if ($null -ne $package) {
                $package.InstallLocation
                $package.PackageFamilyName
            }
            """;
        var encodedScript = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + encodedScript,
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        }) ?? throw new InvalidOperationException("Could not start powershell.exe.");

        var standardOutputTask = process.StandardOutput.ReadToEndAsync();
        var standardErrorTask = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(20_000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("Timed out while querying OpenAI.Codex package location.");
        }

        var standardOutput = standardOutputTask.GetAwaiter().GetResult();
        var standardError = standardErrorTask.GetAwaiter().GetResult();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"Get-AppxPackage OpenAI.Codex failed: {standardError.Trim()}");

        var lines = standardOutput.Split(
            [Environment.NewLine],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length < 2)
            throw Xunit.Sdk.SkipException.ForSkip("OpenAI.Codex package is not installed for the current user.");

        return new CodexPackageInfo(lines[^2], lines[^1]);
    }

    private static HashSet<int> WaitForNewCurrentUserCodexProcessIds(
        string currentUserSid,
        string expectedExecutablePath,
        HashSet<int> initialCodexPids)
    {
        var deadline = DateTime.UtcNow + ProcessAppearanceTimeout;
        do
        {
            var currentPids = GetCurrentUserCodexProcessIds(currentUserSid, expectedExecutablePath);
            currentPids.ExceptWith(initialCodexPids);
            if (currentPids.Count > 0)
                return currentPids;

            Thread.Sleep(100);
        } while (DateTime.UtcNow < deadline);

        return [];
    }

    private static HashSet<int> GetCurrentUserCodexProcessIds(string currentUserSid, string expectedExecutablePath)
    {
        var result = new HashSet<int>();
        var processes = NativeTokenHelper.GetProcessesByNameInCurrentSession(CodexProcessName);
        try
        {
            foreach (var process in processes)
            {
                if (IsExpectedCodexAppxProcess(process, currentUserSid, expectedExecutablePath))
                    result.Add(process.Id);
            }
        }
        finally
        {
            foreach (var process in processes)
                process.Dispose();
        }

        return result;
    }

    private static bool IsExpectedCodexAppxProcess(Process process, string currentUserSid, string expectedExecutablePath)
    {
        try
        {
            return string.Equals(
                       NativeTokenHelper.TryGetProcessOwnerSid((uint)process.Id)?.Value,
                       currentUserSid,
                       StringComparison.OrdinalIgnoreCase)
                   && string.Equals(
                       process.MainModule?.FileName,
                       expectedExecutablePath,
                       StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static void TerminateCurrentUserCodexProcessesExcept(
        string currentUserSid,
        string expectedExecutablePath,
        HashSet<int> preservedPids)
    {
        var processes = NativeTokenHelper.GetProcessesByNameInCurrentSession(CodexProcessName);
        try
        {
            foreach (var process in processes)
            {
                try
                {
                    if (preservedPids.Contains(process.Id)
                        || !IsExpectedCodexAppxProcess(process, currentUserSid, expectedExecutablePath))
                    {
                        continue;
                    }

                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(5_000);
                }
                catch (InvalidOperationException)
                {
                }
            }
        }
        finally
        {
            foreach (var process in processes)
                process.Dispose();
        }
    }

    private static void RunOnSta(Action action)
    {
        ExceptionDispatchInfo? captured = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                captured = ExceptionDispatchInfo.Capture(ex);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        captured?.Throw();
    }

    private readonly record struct CodexPackageInfo(string InstallLocation, string PackageFamilyName);
}
