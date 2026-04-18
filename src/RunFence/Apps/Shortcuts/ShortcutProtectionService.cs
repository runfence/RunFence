using System.Security.AccessControl;
using System.Security.Principal;
using RunFence.Acl;
using RunFence.Core;

namespace RunFence.Apps.Shortcuts;

public class ShortcutProtectionService(ILoggingService log, IAclAccessor aclAccessor) : IShortcutProtectionService
{
    public void ProtectShortcut(string shortcutPath)
    {
        if (!File.Exists(shortcutPath))
            return;

        try
        {
            var attrs = File.GetAttributes(shortcutPath);
            if ((attrs & FileAttributes.ReadOnly) == 0)
                File.SetAttributes(shortcutPath, attrs | FileAttributes.ReadOnly);

            var fileInfo = new FileInfo(shortcutPath);
            var security = fileInfo.GetAccessControl();

            var everyoneSid = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
            const FileSystemRights desiredRights = FileSystemRights.Delete | FileSystemRights.Write |
                                                   FileSystemRights.WriteAttributes | FileSystemRights.AppendData;

            bool hasDenyRule = false;
            foreach (FileSystemAccessRule rule in security.GetAccessRules(true, false, typeof(SecurityIdentifier)))
            {
                if (rule.AccessControlType == AccessControlType.Deny &&
                    rule.IdentityReference is SecurityIdentifier sid &&
                    sid.Equals(everyoneSid) &&
                    (rule.FileSystemRights & desiredRights) == desiredRights)
                {
                    hasDenyRule = true;
                    break;
                }
            }

            if (!hasDenyRule)
            {
                security.AddAccessRule(new FileSystemAccessRule(everyoneSid, desiredRights, AccessControlType.Deny));
                fileInfo.SetAccessControl(security);
            }
        }
        catch (Exception ex)
        {
            log.Error($"Failed to protect shortcut: {shortcutPath}", ex);
        }
    }

    public void UnprotectShortcut(string shortcutPath)
    {
        if (!File.Exists(shortcutPath))
            return;

        try
        {
            var everyoneSid = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
            aclAccessor.RemoveExplicitAces(shortcutPath, everyoneSid.Value, AccessControlType.Deny);
            File.SetAttributes(shortcutPath, File.GetAttributes(shortcutPath) & ~FileAttributes.ReadOnly);
        }
        catch (Exception ex)
        {
            log.Error($"Failed to unprotect shortcut: {shortcutPath}", ex);
        }
    }

    /// <summary>
    /// Applies stricter ACL: LocalSystem=FullControl, Administrators=FullControl, accountSid=ReadAndExecute.
    /// Used for shortcuts created inside managed folder apps. Only the specific account that
    /// owns the app entry gets access — not the entire Users group, since membership is not guaranteed.
    /// </summary>
    public void ProtectInternalShortcut(string shortcutPath, string accountSid)
    {
        if (!File.Exists(shortcutPath))
            return;

        try
        {
            var systemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
            var adminsSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
            var accountIdentity = new SecurityIdentifier(accountSid);
            // Include BuiltinUsersSid so any legacy ACEs from the old code get detected and removed.
            var legacyUsersSid = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
            var everyoneSid = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
            var managedSids = new HashSet<SecurityIdentifier> { systemSid, adminsSid, accountIdentity, legacyUsersSid, everyoneSid };

            var desiredSet = new HashSet<(string sid, FileSystemRights rights, AccessControlType type)>
            {
                (systemSid.Value, FileSystemRights.FullControl, AccessControlType.Allow),
                (adminsSid.Value, FileSystemRights.FullControl, AccessControlType.Allow),
                (accountIdentity.Value, FileSystemRights.ReadAndExecute, AccessControlType.Allow),
            };

            var fileInfo = new FileInfo(shortcutPath);
            var security = fileInfo.GetAccessControl();
            var inheritanceBroken = security.AreAccessRulesProtected;

            var existingSet = new HashSet<(string sid, FileSystemRights rights, AccessControlType type)>();
            var existingManaged = new List<FileSystemAccessRule>();
            foreach (FileSystemAccessRule rule in security.GetAccessRules(true, false, typeof(SecurityIdentifier)))
            {
                if (rule.IdentityReference is SecurityIdentifier sid && managedSids.Contains(sid))
                {
                    existingManaged.Add(rule);
                    // Strip Synchronize from allow ACEs: Windows may auto-add it on read-back,
                    // so ignore it to avoid false "changed" detection.
                    var rights = rule.AccessControlType == AccessControlType.Allow
                        ? rule.FileSystemRights & ~FileSystemRights.Synchronize
                        : rule.FileSystemRights;
                    existingSet.Add((sid.Value, rights, rule.AccessControlType));
                }
            }

            bool aclChanged = !inheritanceBroken || !desiredSet.SetEquals(existingSet);
            if (aclChanged)
            {
                if (!inheritanceBroken)
                    security.SetAccessRuleProtection(true, false);

                if (!desiredSet.SetEquals(existingSet))
                {
                    foreach (var rule in existingManaged)
                        security.RemoveAccessRuleSpecific(rule);
                    security.AddAccessRule(new FileSystemAccessRule(systemSid, FileSystemRights.FullControl, AccessControlType.Allow));
                    security.AddAccessRule(new FileSystemAccessRule(adminsSid, FileSystemRights.FullControl, AccessControlType.Allow));
                    security.AddAccessRule(new FileSystemAccessRule(accountIdentity, FileSystemRights.ReadAndExecute, AccessControlType.Allow));
                }

                fileInfo.SetAccessControl(security);
            }

            var attrs = File.GetAttributes(shortcutPath);
            if ((attrs & FileAttributes.ReadOnly) == 0)
                File.SetAttributes(shortcutPath, attrs | FileAttributes.ReadOnly);
        }
        catch (Exception ex)
        {
            log.Error($"Failed to protect internal shortcut: {shortcutPath}", ex);
        }
    }
}
