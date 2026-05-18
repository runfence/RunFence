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
        if (logFilePath == null)
            return new SettingsTransferResult(false, "Secure log workspace verification failed. Transfer aborted.");
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
            TryDeleteLogFile(logFilePath);
            return new SettingsTransferResult(false, "Stored credentials are incorrect.");
        }
        catch (Exception ex)
        {
            log.Error("Operation failed to launch", ex);
            TryDeleteLogFile(logFilePath);
            return new SettingsTransferResult(false, $"Operation failed: {ex.Message}");
        }

        var result = HandleProcess(process, timeoutMs, logFilePath, pollCallback);
        if (result.Success)
            TryDeleteLogFile(logFilePath);
        else
            log.Info($"PrefTrans: log file retained for troubleshooting at {logFilePath}");
        return result;
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

    private string? MakeLogFilePath(string accountSid)
    {
        var workspace = Path.Combine(PathConstants.ProgramDataDir, "RunFence", "PrefTransLogs");
        try
        {
            Directory.CreateDirectory(workspace);
            VerifySecureWorkspace(workspace);
            EnsureWorkspaceAcl(workspace);
            var logFilePath = Path.Combine(workspace, $"rfn_preftrans_{Guid.NewGuid():N}.log");
            CreateRestrictedLogFile(logFilePath, accountSid);
            return logFilePath;
        }
        catch (Exception ex)
        {
            log.Error("PrefTransLauncher: secure log workspace creation failed", ex);
            return null;
        }
    }

    /// <summary>
    /// Creates the log file with a restrictive ACL: Administrators full control,
    /// the target account write access only, inheritance disabled.
    /// In admin-operation mock mode, the current process SID also gets FullControl so the
    /// non-elevated debug process can still access the log file it created.
    /// Prevents unrelated users from reading potentially sensitive transfer data
    /// while allowing the preftrans process (running as <paramref name="accountSid"/>)
    /// to write its output, and preserving admin read access for troubleshooting.
    /// Fail-closed: ACL failure aborts the operation.
    /// </summary>
    private void CreateRestrictedLogFile(string path, string accountSid)
    {
        var security = new FileSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

        var admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        security.AddAccessRule(new FileSystemAccessRule(admins, FileSystemRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(system, FileSystemRights.FullControl, AccessControlType.Allow));
        AdminOperationMockAccessHelper.AddCurrentProcessFileSystemAccess(security, FileSystemRights.FullControl);

        var targetAccount = new SecurityIdentifier(accountSid);
        security.AddAccessRule(new FileSystemAccessRule(
            targetAccount,
            FileSystemRights.WriteData | FileSystemRights.AppendData | FileSystemRights.ReadAttributes | FileSystemRights.Synchronize,
            AccessControlType.Allow));
        using var _ = FileSystemAclExtensions.Create(
            new FileInfo(path),
            FileMode.CreateNew,
            FileSystemRights.WriteData | FileSystemRights.AppendData | FileSystemRights.ReadAttributes | FileSystemRights.Synchronize,
            FileShare.ReadWrite,
            4096,
            FileOptions.None,
            security);
    }

    private static void EnsureWorkspaceAcl(string workspacePath)
    {
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        var admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        security.AddAccessRule(new FileSystemAccessRule(admins, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(system, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        AdminOperationMockAccessHelper.AddCurrentProcessFileSystemAccess(
            security,
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None);
        new DirectoryInfo(workspacePath).SetAccessControl(security);

        var applied = new DirectoryInfo(workspacePath).GetAccessControl(AccessControlSections.Access);
        if (!applied.AreAccessRulesProtected)
            throw new InvalidOperationException("Log workspace ACL is not protected.");
    }

    private static void VerifySecureWorkspace(string workspacePath)
    {
        var expectedRoot = Path.Combine(PathConstants.ProgramDataDir, "RunFence");
        var fullWorkspacePath = Path.GetFullPath(workspacePath);
        var fullExpectedRoot = Path.GetFullPath(expectedRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var candidate = fullWorkspacePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(fullExpectedRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Log workspace is outside the RunFence ProgramData root.");

        var info = new DirectoryInfo(fullWorkspacePath);
        if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("Log workspace must not be a reparse point.");
    }

    private void TryDeleteLogFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
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
