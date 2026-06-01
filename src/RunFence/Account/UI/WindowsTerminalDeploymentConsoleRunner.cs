using System.Diagnostics;
using RunFence.Core;
using RunFence.Infrastructure;
using System.Text;

namespace RunFence.Account.UI;

public interface IWindowsTerminalDeploymentConsoleRunner
{
    Task DownloadAsync(WindowsTerminalPackageDownloadOperation operation, CancellationToken cancellationToken);

    Task RunAsync(WindowsTerminalDeploymentOperation operation, CancellationToken cancellationToken);
}

public sealed class WindowsTerminalDeploymentConsoleRunner(
    IProcessExecutionService processExecutionService,
    WindowsTerminalDeploymentScriptBuilder scriptBuilder,
    IAppLockControl appLockControl)
    : IWindowsTerminalDeploymentConsoleRunner
{
    private static readonly TimeSpan DeploymentTimeout = TimeSpan.FromMinutes(30);

    public Task DownloadAsync(WindowsTerminalPackageDownloadOperation operation, CancellationToken cancellationToken)
        => RunScriptAsync(scriptBuilder.BuildDownloadScript(operation), cancellationToken);

    public Task RunAsync(WindowsTerminalDeploymentOperation operation, CancellationToken cancellationToken)
        => RunScriptAsync(scriptBuilder.BuildDeploymentScript(operation), cancellationToken);

    private async Task RunScriptAsync(string script, CancellationToken cancellationToken)
    {
        ThrowIfLocked();
        var tempScriptPath = Path.Combine(Path.GetTempPath(), $"runfence-windows-terminal-{Guid.NewGuid():N}.ps1");
        await File.WriteAllTextAsync(tempScriptPath, script, new UTF8Encoding(false), cancellationToken)
            .ConfigureAwait(false);

        try
        {
            ThrowIfLocked();
            var powerShellPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                @"WindowsPowerShell\v1.0\powershell.exe");
            var result = await processExecutionService.RunAsync(new ProcessExecutionRequest(
                FileName: powerShellPath,
                Arguments: CommandLineHelper.JoinArgs(["-NoLogo", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", tempScriptPath])
                           ?? string.Empty,
                Timeout: DeploymentTimeout,
                KillEntireProcessTreeOnTimeout: true,
                RedirectStandardOutput: false,
                RedirectStandardError: false,
                CancellationToken: cancellationToken,
                UseShellExecute: true,
                WindowStyle: ProcessWindowStyle.Normal)).ConfigureAwait(false);

            if (!result.Started)
                throw new InvalidOperationException(result.FailureMessage ?? "Failed to start the Windows Terminal deployment console.");
            if (result.TimedOut)
                throw new InvalidOperationException("Windows Terminal shared deployment timed out.");
            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    "Windows Terminal shared deployment failed or was canceled before completion.");
            }
        }
        finally
        {
            try
            {
                File.Delete(tempScriptPath);
            }
            catch (Exception)
            {
            }
        }
    }

    private void ThrowIfLocked()
    {
        if (appLockControl.IsLocked)
            throw new OperationCanceledException("Windows Terminal deployment console is disabled while RunFence is locked.");
    }
}
