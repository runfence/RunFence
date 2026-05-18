using System.Security.AccessControl;
using System.Security.Principal;
using RunFence.Acl.Traverse;
using RunFence.Core;

namespace RunFence.Account;

/// <summary>
/// Manages per-user MLGPO (Multiple Local Group Policy Objects) logon scripts
/// under System32\GroupPolicyUsers\{SID}\User\Scripts\scripts.ini.
/// Blocks login by registering logoff.exe as a logon script for the target user.
/// </summary>
public class GroupPolicyScriptHelper : IGroupPolicyScriptHelper
{
    private const string LogoffCmdLine = "logoff.exe";
    private const string ScriptFileName = "block_login.cmd";

    private readonly string _gpUsersDir;
    private readonly string _scriptsDir;
    private readonly string _legacyScriptsDir;
    private readonly ILoggingService _log;
    private readonly ILogonScriptTraverseGranter? _traverseGranter;
    private readonly LogonScriptIniManager _iniManager;

    public GroupPolicyScriptHelper(LogonScriptIniManager iniManager, ILoggingService log,
        ILogonScriptTraverseGranter? traverseGranter = null, string? systemDir = null,
        string? scriptsDir = null, string? legacyScriptsDir = null)
    {
        var sysDir = systemDir ?? Environment.GetFolderPath(Environment.SpecialFolder.System);
        _gpUsersDir = Path.Combine(sysDir, "GroupPolicyUsers");
        _scriptsDir = scriptsDir ?? Path.Combine(PathConstants.ProgramDataDir, "scripts");
        _legacyScriptsDir = legacyScriptsDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "RunAsManager", "scripts");
        _log = log;
        _traverseGranter = traverseGranter;
        _iniManager = iniManager;
    }

    private string GetIniPath(string sid) =>
        Path.Combine(_gpUsersDir, sid, "User", "Scripts", "scripts.ini");

    private string GetGptIniPath(string sid) =>
        Path.Combine(_gpUsersDir, sid, "gpt.ini");

    public bool IsLoginBlocked(string sid)
    {
        var iniPath = GetIniPath(sid);
        if (!File.Exists(iniPath))
            return false;

        var wrapperPath = GetWrapperScriptPath(sid);
        var legacyWrapperPath = GetLegacyWrapperScriptPath(sid);

        bool inLogon = false;
        foreach (var line in File.ReadAllLines(iniPath))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith('['))
            {
                inLogon = trimmed.Equals("[Logon]", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inLogon)
                continue;

            var match = LogonScriptIniManager.CmdLineValueRegex().Match(trimmed);
            if (match.Success)
            {
                var cmdLine = match.Groups[1].Value;
                // Detect old bare logoff.exe, new-style wrapper, and legacy RunAsManager wrapper
                if (cmdLine.Equals(LogoffCmdLine, StringComparison.OrdinalIgnoreCase) ||
                    cmdLine.Equals(wrapperPath, StringComparison.OrdinalIgnoreCase) ||
                    cmdLine.Equals(legacyWrapperPath, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return false;
    }

    private string GetWrapperScriptPath(string sid) =>
        Path.Combine(_scriptsDir, $"{sid}_{ScriptFileName}");

    private string GetLegacyWrapperScriptPath(string sid) =>
        Path.Combine(_legacyScriptsDir, $"{sid}_{ScriptFileName}");

    public SetLoginBlockedResult SetLoginBlocked(string sid, bool blocked)
    {
        var iniPath = GetIniPath(sid);
        var wrapperPath = GetWrapperScriptPath(sid);
        var legacyWrapperPath = GetLegacyWrapperScriptPath(sid);

        if (blocked)
        {
            if (IsLoginBlocked(sid))
                return new SetLoginBlockedResult(wrapperPath, null);

            try
            {
                EnsureWrapperScript(wrapperPath, sid);
                _iniManager.AppendLogonEntry(iniPath, wrapperPath);
                _iniManager.UpdateGptIni(GetGptIniPath(sid), hasScripts: true);

                var traversePaths = _traverseGranter?.GrantTraverseAccess(sid, _scriptsDir);
                return new SetLoginBlockedResult(wrapperPath, traversePaths);
            }
            catch (Exception ex)
            {
                TryRollbackLogonScriptState(
                    () => RemoveBlockedStateArtifacts(sid, iniPath, wrapperPath, legacyWrapperPath, strictRollback: true),
                    ex);
            }
        }

        try
        {
            RemoveBlockedStateArtifacts(sid, iniPath, wrapperPath, legacyWrapperPath, strictRollback: false);
        }
        catch (Exception ex)
        {
            TryRollbackLogonScriptState(
                () => RestoreBlockedStateArtifacts(sid, iniPath, wrapperPath),
                ex);
        }

        // Return the script path so callers with a database can remove the corresponding
        // AccountGrants entries (both the script grant and the scripts-dir traverse entry).
        return new SetLoginBlockedResult(wrapperPath, null);
    }

    private void RemoveBlockedStateArtifacts(string sid, string iniPath, string wrapperPath, string legacyWrapperPath, bool strictRollback)
    {
        if (File.Exists(iniPath))
            _iniManager.RemoveLogonEntry(iniPath, wrapperPath, legacyWrapperPath);
        // scripts.ini deleted by RemoveLogonEntry when no entries remain
        _iniManager.UpdateGptIni(GetGptIniPath(sid), hasScripts: File.Exists(iniPath));
        DeleteWrapperIfExists(wrapperPath, strictRollback);
        DeleteWrapperIfExists(legacyWrapperPath, strictRollback);

        try
        {
            _traverseGranter?.RevokeTraverseAccess(sid, _scriptsDir);
        }
        catch (Exception ex)
        {
            if (strictRollback)
                throw;

            _log.Warn($"GroupPolicyScriptHelper: failed to revoke traverse access for '{sid}' on '{_scriptsDir}': {ex.Message}");
        }
    }

    private void RestoreBlockedStateArtifacts(string sid, string iniPath, string wrapperPath)
    {
        EnsureWrapperScript(wrapperPath, sid);
        _iniManager.AppendLogonEntry(iniPath, wrapperPath);
        _iniManager.UpdateGptIni(GetGptIniPath(sid), hasScripts: true);
        _traverseGranter?.GrantTraverseAccess(sid, _scriptsDir);
    }

    private void DeleteWrapperIfExists(string wrapperPath, bool strictRollback)
    {
        try
        {
            if (File.Exists(wrapperPath))
                File.Delete(wrapperPath);
        }
        catch
        {
            if (strictRollback)
                throw;
        }
    }

    private void TryRollbackLogonScriptState(Action rollback, Exception ex)
    {
        try
        {
            rollback();
        }
        catch (Exception rollbackEx)
        {
            throw new AccountRestrictionOperationException(
                $"{ex.Message} Rollback failed: {rollbackEx.Message}",
                AccountRestrictionStatus.Failed,
                rollbackAttempted: true,
                ex);
        }

        throw new AccountRestrictionOperationException(
            ex.Message,
            AccountRestrictionStatus.RolledBack,
            rollbackAttempted: true,
            ex);
    }

    private void EnsureWrapperScript(string scriptPath, string userSid)
    {
        var scriptDir = Path.GetDirectoryName(scriptPath)!;
        Directory.CreateDirectory(scriptDir);
        SecureScriptsDirectory(scriptDir);

        var tmpPath = scriptPath + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
        File.WriteAllText(tmpPath, "@echo off\r\nlogoff.exe\r\n");

        // Secure the temp file before moving it to the target location
        var tmpInfo = new FileInfo(tmpPath);
        var security = tmpInfo.GetAccessControl();
        security.SetAccessRuleProtection(true, false);

        var systemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        security.AddAccessRule(new FileSystemAccessRule(
            systemSid, FileSystemRights.FullControl, AccessControlType.Allow));

        var adminSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        security.AddAccessRule(new FileSystemAccessRule(
            adminSid, FileSystemRights.FullControl, AccessControlType.Allow));

        // Ensure the current process user retains FullControl so the file can be moved after
        // SetAccessControl strips inherited ACEs (needed when running as non-admin, e.g. in tests).
        var currentUser = WindowsIdentity.GetCurrent().User;
        if (currentUser != null)
            security.AddAccessRule(new FileSystemAccessRule(
                currentUser, FileSystemRights.FullControl, AccessControlType.Allow));

        // Grant only the specific user whose login is being blocked — not the entire Users group,
        // since membership is not guaranteed (e.g. custom or restricted accounts).
        var targetUserSid = new SecurityIdentifier(userSid);
        security.AddAccessRule(new FileSystemAccessRule(
            targetUserSid, FileSystemRights.ReadAndExecute, AccessControlType.Allow));

        tmpInfo.SetAccessControl(security);

        File.Move(tmpPath, scriptPath, overwrite: true);
    }

    private void SecureScriptsDirectory(string dirPath)
    {
        try
        {
            var dirInfo = new DirectoryInfo(dirPath);
            var security = dirInfo.GetAccessControl();

            // Only apply if inheritance is not already broken (idempotent).
            if (security.AreAccessRulesProtected)
                return;

            security.SetAccessRuleProtection(true, false);

            var systemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
            security.AddAccessRule(new FileSystemAccessRule(
                systemSid, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));

            // Grant the elevated admin process user explicit FullControl.
            var currentUser = WindowsIdentity.GetCurrent().User;
            if (currentUser != null)
            {
                security.AddAccessRule(new FileSystemAccessRule(
                    currentUser, FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    PropagationFlags.None,
                    AccessControlType.Allow));
            }

            var adminSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
            security.AddAccessRule(new FileSystemAccessRule(
                adminSid, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));

            // No shared ACE on the scripts directory itself — each user's script file
            // gets a per-user ReadAndExecute ACE in EnsureWrapperScript, and traverse
            // access on this directory and its ancestors is granted individually via
            // LogonScriptTraverseGranter when login blocking is activated.

            dirInfo.SetAccessControl(security);
        }
        catch (Exception ex)
        {
            _log.Warn($"Failed to secure scripts directory: {ex.Message}");
        }
    }
}
