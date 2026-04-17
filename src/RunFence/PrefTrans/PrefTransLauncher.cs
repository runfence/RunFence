using System.ComponentModel;
using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using RunFence.Core;
using RunFence.Launch;
using RunFence.Launch.Tokens;

namespace RunFence.PrefTrans;

public class PrefTransLauncher(ILaunchFacade facade, ILoggingService log) : IPrefTransLauncher
{
    public SettingsTransferResult RunAndWait(string prefTransPath, string command, string filePath,
        string accountSid, int timeoutMs, Action? pollCallback)
    {
        var logFilePath = MakeLogFilePath(accountSid);
        try
        {
            ProcessInfo process;
            try
            {
                var identity = new AccountLaunchIdentity(accountSid);
                var target = new ProcessLaunchTarget(prefTransPath,
                    [command, filePath, "--logfile", logFilePath], HideWindow: true);
                process = facade.LaunchFile(target, identity)!;
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == ProcessLaunchNative.Win32ErrorLogonFailure)
            {
                log.Error($"Settings operation credential failure for {accountSid}", ex);
                return new SettingsTransferResult(false, "Stored credentials are incorrect.");
            }
            catch (Exception ex)
            {
                log.Error("Operation failed to launch", ex);
                return new SettingsTransferResult(false, $"Operation failed: {ex.Message}");
            }

            return HandleProcess(process, timeoutMs, logFilePath, pollCallback);
        }
        finally
        {
            try
            {
                File.Delete(logFilePath);
            }
            catch
            {
            }
        }
    }

    private SettingsTransferResult HandleProcess(ProcessInfo process, int timeoutMs, string logFilePath, Action? pollCallback)
    {
        using (process)
        {
            var sw = Stopwatch.StartNew();
            while (!process.WaitForExit(500))
            {
                if (sw.ElapsedMilliseconds > timeoutMs)
                {
                    process.Kill(1);
                    process.WaitForExit(2000);
                    log.Error("Operation timed out");
                    return new(false, "Operation timed out");
                }

                pollCallback?.Invoke();
            }

            if (process.ExitCode == 0)
            {
                log.Info("Operation succeeded");
                return new(true, "");
            }

            log.Error($"Operation failed: exit code {process.ExitCode}");
            var logContent = ReadLogFile(logFilePath);
            if (!string.IsNullOrEmpty(logContent))
                return new(false, logContent);
            return new(false, "Error code: " + process.ExitCode);
        }
    }

    private string MakeLogFilePath(string accountSid)
    {
        var sharedTempDir = SettingsTransferService.GetSharedTempDir();
        Directory.CreateDirectory(sharedTempDir);
        var logFilePath = Path.Combine(sharedTempDir, $"rfn_preftrans_{Guid.NewGuid():N}.log");
        CreateRestrictedLogFile(logFilePath, accountSid);
        return logFilePath;
    }

    /// <summary>
    /// Creates the log file with a restrictive ACL: Administrators full control,
    /// the target account write access only, inheritance disabled.
    /// Prevents unrelated users from reading potentially sensitive transfer data
    /// while allowing the preftrans process (running as <paramref name="accountSid"/>)
    /// to write its output, and preserving admin read access for troubleshooting.
    /// Falls back to an unprotected file on ACL failure — the operation continues.
    /// </summary>
    private void CreateRestrictedLogFile(string path, string accountSid)
    {
        File.WriteAllBytes(path, []);

        try
        {
            var security = new FileSecurity();
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

            var admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
            security.AddAccessRule(new FileSystemAccessRule(
                admins,
                FileSystemRights.FullControl,
                AccessControlType.Allow));

            // Grant the target account write-only access so preftrans can write its log output.
            // ReadAttributes and Synchronize are required for basic file open/close operations.
            var targetAccount = new SecurityIdentifier(accountSid);
            security.AddAccessRule(new FileSystemAccessRule(
                targetAccount,
                FileSystemRights.WriteData | FileSystemRights.AppendData
                    | FileSystemRights.ReadAttributes | FileSystemRights.Synchronize,
                AccessControlType.Allow));

            new FileInfo(path).SetAccessControl(security);
        }
        catch (Exception ex)
        {
            log.Warn($"PrefTransLauncher: Failed to set restrictive ACL on log file: {ex.Message}");
        }
    }

    private string ReadLogFile(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path).Trim() : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}
