using System.Security.AccessControl;
using System.Security.Principal;
using RunFence.Acl;
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
    private const string LegacyScriptDirName = "scripts";

    private readonly string _gpUsersDir;
    private readonly string _scriptsDir;
    private readonly string _legacyScriptsDir;
    private readonly ILoggingService _log;
    private readonly ILogonScriptTraverseGranter? _traverseGranter;
    private readonly LogonScriptIniManager _iniManager;
    private readonly IProgramDataDirectoryProvisioningService _programDataDirectoryProvisioningService;
    private readonly IProgramDataManagedObjectRepairService _programDataManagedObjectRepairService;
    private readonly IProgramDataPathPolicyService _programDataPathPolicyService;
    private readonly IProgramDataObjectProvisioner _programDataObjectProvisioner;
    private readonly string _knownScriptsDir;
    private readonly LogonScriptStateRollbackStore _rollbackStore;

    public GroupPolicyScriptHelper(
        LogonScriptIniManager iniManager,
        LogonScriptStateRollbackStore rollbackStore,
        ILoggingService log,
        IProgramDataDirectoryProvisioningService programDataDirectoryProvisioningService,
        IProgramDataManagedObjectRepairService programDataManagedObjectRepairService,
        IProgramDataPathPolicyService programDataPathPolicyService,
        IProgramDataObjectProvisioner programDataObjectProvisioner,
        IProgramDataKnownPathResolver programDataKnownPathResolver,
        ILogonScriptTraverseGranter? traverseGranter = null,
        string? systemDir = null,
        string? scriptsDir = null,
        string? legacyScriptsDir = null)
    {
        var sysDir = systemDir ?? Environment.GetFolderPath(Environment.SpecialFolder.System);
        _gpUsersDir = Path.Combine(sysDir, "GroupPolicyUsers");
        _knownScriptsDir = programDataKnownPathResolver.GetDirectoryPath(ProgramDataPolicies.Scripts);
        _scriptsDir = scriptsDir ?? _knownScriptsDir;
        _legacyScriptsDir = legacyScriptsDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "RunAsManager", LegacyScriptDirName);
        _log = log;
        _traverseGranter = traverseGranter;
        _iniManager = iniManager;
        _programDataDirectoryProvisioningService = programDataDirectoryProvisioningService;
        _programDataManagedObjectRepairService = programDataManagedObjectRepairService;
        _programDataPathPolicyService = programDataPathPolicyService;
        _programDataObjectProvisioner = programDataObjectProvisioner;
        _rollbackStore = rollbackStore;
    }

    private string GetIniPath(string sid) =>
        Path.Combine(_gpUsersDir, sid, "User", "Scripts", "scripts.ini");

    private string GetGptIniPath(string sid) =>
        Path.Combine(_gpUsersDir, sid, "gpt.ini");

    public bool IsLoginBlocked(string sid)
        => GetLogonBlockEntryState(sid).IsBlocked;

    private LogonBlockEntryState GetLogonBlockEntryState(string sid)
    {
        var iniPath = GetIniPath(sid);
        if (!File.Exists(iniPath))
            return new LogonBlockEntryState(false, false);

        var wrapperPath = GetWrapperScriptPath(sid);
        var legacyWrapperPath = GetLegacyWrapperScriptPath(sid);

        bool inLogon = false;
        bool hasCurrentWrapper = false;
        bool hasLegacyOrBare = false;
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
                if (cmdLine.Equals(wrapperPath, StringComparison.OrdinalIgnoreCase))
                    hasCurrentWrapper = true;
                else if (cmdLine.Equals(LogoffCmdLine, StringComparison.OrdinalIgnoreCase) ||
                         cmdLine.Equals(legacyWrapperPath, StringComparison.OrdinalIgnoreCase))
                    hasLegacyOrBare = true;
            }
        }

        return new LogonBlockEntryState(hasCurrentWrapper, hasLegacyOrBare);
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
        var gptIniPath = GetGptIniPath(sid);

        if (blocked)
        {
            var blockEntryState = GetLogonBlockEntryState(sid);

            if (blockEntryState.IsCurrentWrapperOnly)
            {
                var snapshot = _rollbackStore.Capture(iniPath, gptIniPath, wrapperPath, legacyWrapperPath);
                try
                {
                    EnsureWrapperScript(wrapperPath, sid);
                    var traversePaths = _traverseGranter?.GrantTraverseAccess(sid, _scriptsDir);

                    return new SetLoginBlockedResult(wrapperPath, traversePaths);
                }
                catch (Exception ex)
                {
                    TryRollbackLogonScriptState(() => _rollbackStore.Restore(snapshot), ex);
                }
            }

            if (blockEntryState.IsBlocked)
            {
                var snapshot = _rollbackStore.Capture(iniPath, gptIniPath, wrapperPath, legacyWrapperPath);
                try
                {
                    EnsureWrapperScript(wrapperPath, sid);
                    var traversePaths = _traverseGranter?.GrantTraverseAccess(sid, _scriptsDir);
                    _iniManager.RemoveLogonEntry(iniPath, wrapperPath, legacyWrapperPath);
                    _iniManager.AppendLogonEntry(iniPath, wrapperPath);
                    _iniManager.UpdateGptIni(gptIniPath, hasScripts: true);

                    return new SetLoginBlockedResult(wrapperPath, traversePaths);
                }
                catch (Exception ex)
                {
                    TryRollbackLogonScriptState(() => _rollbackStore.Restore(snapshot), ex);
                }
            }

            try
            {
                EnsureWrapperScript(wrapperPath, sid);

                var traversePaths = _traverseGranter?.GrantTraverseAccess(sid, _scriptsDir);
                _iniManager.AppendLogonEntry(iniPath, wrapperPath);
                _iniManager.UpdateGptIni(gptIniPath, hasScripts: true);

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

    private void RemoveBlockedStateArtifacts(
        string sid,
        string iniPath,
        string wrapperPath,
        string legacyWrapperPath,
        bool strictRollback)
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
        var isProgramDataScript = _programDataPathPolicyService.IsUnderRoot(scriptDir);
        if (isProgramDataScript)
        {
            if (string.Equals(
                    Path.GetFullPath(scriptDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    Path.GetFullPath(_knownScriptsDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase))
            {
                _programDataDirectoryProvisioningService.EnsureKnownDirectory(ProgramDataPolicies.Scripts);
            }
            else
            {
                _programDataObjectProvisioner.CreateOrRepairDirectory(
                    new ProgramDataExplicitDirectoryRequest(
                        scriptDir,
                        ProgramDataDirectoryAclProfile.CurrentProcessUserFullControl,
                        [],
                        ReplaceExistingSecurity: true));
            }
        }
        else
        {
            Directory.CreateDirectory(scriptDir);
            SecureScriptsDirectory(scriptDir);
        }

        var tmpPath = scriptPath + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
        try
        {
            if (isProgramDataScript)
            {
                _programDataObjectProvisioner.CreateFile(
                    new ProgramDataExplicitFileRequest(
                        tmpPath,
                        ProgramDataFileAclProfile.CurrentProcessUserFullControl,
                        [CreateFileAccess(userSid, FileSystemRights.ReadAndExecute)],
                    FileShare.Read,
                    OverwriteExisting: false),
                    stream =>
                    {
                        using var writer = new StreamWriter(stream, System.Text.Encoding.UTF8, 1024, leaveOpen: true);
                        writer.Write("@echo off\r\nlogoff.exe\r\n");
                    });
            }
            else
            {
                using var fs = CreateRestrictedFile(tmpPath, userSid);
                using var writer = new StreamWriter(fs, System.Text.Encoding.UTF8);
                writer.Write("@echo off\r\nlogoff.exe\r\n");
            }

            File.Move(tmpPath, scriptPath, overwrite: true);
        }
        catch
        {
            try
            {
                if (File.Exists(tmpPath))
                    File.Delete(tmpPath);
            }
            catch (Exception cleanupEx)
            {
                _log.Warn($"Failed to delete temporary logon script '{tmpPath}': {cleanupEx.Message}");
            }

            throw;
        }

        if (_programDataPathPolicyService.IsUnderRoot(scriptPath))
        {
            _programDataManagedObjectRepairService.EnsureManagedFileOwner(scriptPath);
        }
    }

    private static ProgramDataPrincipalAccess CreateFileAccess(string sid, FileSystemRights rights)
        => new(
            new SecurityIdentifier(sid),
            rights,
            InheritanceFlags.None,
            PropagationFlags.None);

    private FileStream CreateRestrictedFile(string filePath, string userSid)
    {
        var security = new FileSecurity();
        security.SetAccessRuleProtection(true, false);

        var systemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        security.AddAccessRule(new FileSystemAccessRule(
            systemSid, FileSystemRights.FullControl, AccessControlType.Allow));

        var adminSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        security.AddAccessRule(new FileSystemAccessRule(
            adminSid, FileSystemRights.FullControl, AccessControlType.Allow));

        var currentUser = WindowsIdentity.GetCurrent().User;
        if (currentUser != null)
        {
            security.AddAccessRule(new FileSystemAccessRule(
                currentUser, FileSystemRights.FullControl, AccessControlType.Allow));
        }

        // Grant only the specific user whose login is being blocked.
        var targetUserSid = new SecurityIdentifier(userSid);
        security.AddAccessRule(new FileSystemAccessRule(
            targetUserSid, FileSystemRights.ReadAndExecute, AccessControlType.Allow));

        var fileInfo = new FileInfo(filePath);
        var stream = fileInfo.Create(
            FileMode.CreateNew,
            FileSystemRights.ReadData | FileSystemRights.WriteData,
            FileShare.Read,
            4096,
            FileOptions.None,
            security);
        if (_programDataPathPolicyService.IsUnderRoot(filePath))
        {
            _log.Info(
                $"ProgramData security created restricted logon script '{filePath}' with {ProgramDataSecurityChangeFormatter.DescribeSecurityState(security)}.");
        }

        return stream;
    }

    private void SecureScriptsDirectory(string dirPath)
    {
        try
        {
            var dirInfo = new DirectoryInfo(dirPath);
            var security = dirInfo.GetAccessControl();
            if (security.AreAccessRulesProtected)
                return;

            security.SetAccessRuleProtection(true, false);

            var systemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
            security.AddAccessRule(new FileSystemAccessRule(
                systemSid, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));

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

            dirInfo.SetAccessControl(security);
        }
        catch (Exception ex)
        {
            _log.Warn($"Failed to secure scripts directory: {ex.Message}");
        }
    }

    private readonly record struct LogonBlockEntryState(bool HasCurrentWrapper, bool HasLegacyOrBare)
    {
        public bool IsBlocked => HasCurrentWrapper || HasLegacyOrBare;
        public bool IsCurrentWrapperOnly => HasCurrentWrapper && !HasLegacyOrBare;
    }

}
