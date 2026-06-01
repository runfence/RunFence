using System.ComponentModel;
using RunFence.Core;
using RunFence.Launch;
using RunFence.Launch.Tokens;

namespace RunFence.PrefTrans;

public class PrefTransLauncher(
    ILaunchFacade facade,
    ILoggingService log,
    IPrefTransLogWorkspace logWorkspace,
    IPrefTransProcessWaiter processWaiter) : IPrefTransLauncher
{
    public SettingsTransferResult RunAndWait(string prefTransPath, string command, string filePath,
        string accountSid, int timeoutMs, Action? pollCallback)
    {
        var workspaceResult = logWorkspace.CreateLogFile(accountSid);
        if (!workspaceResult.Success || string.IsNullOrEmpty(workspaceResult.LogFilePath))
            return new SettingsTransferResult(false, "Secure log workspace verification failed. Transfer aborted.");
        var logFilePath = workspaceResult.LogFilePath;

        ProcessInfo process;
        try
        {
            var identity = new AccountLaunchIdentity(accountSid);
            var target = new ProcessLaunchTarget(prefTransPath,
                [command, filePath, "--logfile", logFilePath], HideWindow: true, SuppressStartupFeedback: true);
            using var launch = facade.LaunchFile(target, identity);
            var warning = LaunchExecutionWarningFormatter.Format("The transfer helper", launch);
            if (warning != null)
                log.Warn(warning);
            process = launch.DetachProcess()
                      ?? throw new InvalidOperationException($"PrefTrans launch did not return a process handle for '{prefTransPath}'.");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ProcessLaunchNative.Win32ErrorLogonFailure)
        {
            log.Error($"Settings operation credential failure for {accountSid}", ex);
            logWorkspace.TryDeleteLogFile(logFilePath);
            return new SettingsTransferResult(false, "Stored credentials are incorrect.");
        }
        catch (Exception ex)
        {
            log.Error("Operation failed to launch", ex);
            logWorkspace.TryDeleteLogFile(logFilePath);
            return new SettingsTransferResult(false, $"Operation failed: {ex.Message}");
        }

        var result = processWaiter.WaitForResult(process, timeoutMs, logFilePath, pollCallback);
        if (result.Success)
            logWorkspace.TryDeleteLogFile(logFilePath);
        else
            log.Info($"PrefTrans: log file retained for troubleshooting at {logFilePath}");
        return result;
    }
}
