using System.Security.AccessControl;
using System.Security.Principal;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;

namespace RunFence.Acl;

/// <summary>
/// Handles allow-mode ACL operations: applying, reverting, and cleaning up allow-mode ACEs.
/// Extracted from AclService to keep it focused on dispatch.
/// </summary>
public class AclAllowModeService(ILoggingService log, ILocalUserProvider localUserProvider)
{
    public bool ApplyAllowAcl(AppEntry app, string targetPath)
    {
        var isDirectory = Directory.Exists(targetPath);
        var isFile = File.Exists(targetPath);
        if (!isDirectory && !isFile)
            return false;

        var entries = app.AllowedAclEntries;
        if (entries == null || entries.Count == 0)
            return false;

        var systemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        var adminsSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);

        var desiredRules = new List<FileSystemAccessRule>();

        var inhFlags = AclHelper.InheritanceFlagsFor(isDirectory);
        const PropagationFlags propFlags = PropagationFlags.None;

        desiredRules.Add(new FileSystemAccessRule(
            systemSid, FileSystemRights.FullControl,
            inhFlags, propFlags, AccessControlType.Allow));

        desiredRules.Add(new FileSystemAccessRule(
            adminsSid,
            FileSystemRights.ChangePermissions | FileSystemRights.ReadPermissions |
            FileSystemRights.ReadAttributes | FileSystemRights.ReadExtendedAttributes,
            inhFlags, propFlags, AccessControlType.Allow));

        foreach (var entry in entries)
        {
            try
            {
                var sid = new SecurityIdentifier(entry.Sid);
                var rights = FileSystemRights.Read | FileSystemRights.Synchronize;
                if (entry.AllowWrite)
                {
                    rights |= FileSystemRights.WriteData | FileSystemRights.AppendData |
                              FileSystemRights.WriteExtendedAttributes | FileSystemRights.WriteAttributes |
                              FileSystemRights.Delete | FileSystemRights.DeleteSubdirectoriesAndFiles;
                }

                if (entry.AllowExecute)
                {
                    rights |= FileSystemRights.ExecuteFile;
                }

                desiredRules.Add(new FileSystemAccessRule(
                    sid, rights, inhFlags, propFlags, AccessControlType.Allow));
            }
            catch (Exception ex)
            {
                log.Error($"Failed to create allow rule for SID {entry.Sid}", ex);
            }
        }

        var managedSids = new HashSet<SecurityIdentifier> { systemSid, adminsSid };
        foreach (var entry in entries)
        {
            try
            {
                managedSids.Add(new SecurityIdentifier(entry.Sid));
            }
            catch (ArgumentException)
            {
            }
        }

        Func<FileSystemAccessRule, bool> isManagedAce = rule =>
            rule is { AccessControlType: AccessControlType.Allow, IdentityReference: SecurityIdentifier sid } &&
            managedSids.Contains(sid);

        var localUserSids = AclHelper.BuildLocalUserSidSet(localUserProvider.GetLocalUserAccounts());
        bool changed = false;
        AclHelper.ModifyAclIf(targetPath, isDirectory, security =>
        {
            var denyCleaned = AclDenyModeService.RemoveManagedDenyAces(security, localUserSids);
            var inheritanceBroken = security.AreAccessRulesProtected;

            if (!inheritanceBroken)
                security.SetAccessRuleProtection(true, false);

            changed = AclHelper.ApplyAclDiff(security, desiredRules, isManagedAce) || !inheritanceBroken || denyCleaned;
            return changed;
        });

        return changed;
    }

    public void RevertAllowAcl(string targetPath, AppEntry app)
    {
        var isDirectory = Directory.Exists(targetPath);
        var isFile = File.Exists(targetPath);
        if (!isDirectory && !isFile)
            return;

        AclHelper.ModifyAcl(targetPath, isDirectory, security =>
        {
            RemoveAllowManagedAces(security, app);
            security.SetAccessRuleProtection(false, false);
        });
    }

    public void CleanupAllowModeAces(string targetPath, bool isFolder)
    {
        var exists = isFolder ? Directory.Exists(targetPath) : File.Exists(targetPath);
        if (!exists)
            return;

        try
        {
            var changed = AclHelper.ModifyAclIf(targetPath, isFolder, security =>
            {
                if (!security.AreAccessRulesProtected)
                    return false;
                RemoveAllExplicitAllowAces(security);
                security.SetAccessRuleProtection(false, false);
                return true;
            });

            if (changed)
                log.Info($"Cleaned up allow-mode ACEs from {targetPath}");
        }
        catch (Exception ex)
        {
            log.Error($"Failed to clean up allow-mode ACEs from {targetPath}", ex);
        }
    }

    private static void RemoveAllowManagedAces(FileSystemSecurity security, AppEntry app)
    {
        var managedSids = new HashSet<SecurityIdentifier>
        {
            new(WellKnownSidType.LocalSystemSid, null),
            new(WellKnownSidType.BuiltinAdministratorsSid, null)
        };
        if (app.AllowedAclEntries != null)
        {
            foreach (var entry in app.AllowedAclEntries)
            {
                try
                {
                    managedSids.Add(new SecurityIdentifier(entry.Sid));
                }
                catch (ArgumentException)
                {
                }
            }
        }

        var rules = security.GetAccessRules(true, false, typeof(SecurityIdentifier));
        foreach (FileSystemAccessRule rule in rules)
        {
            if (rule.IdentityReference is SecurityIdentifier sid && managedSids.Contains(sid))
                security.RemoveAccessRuleSpecific(rule);
        }
    }

    private static void RemoveAllExplicitAllowAces(FileSystemSecurity security)
    {
        var rules = security.GetAccessRules(true, false, typeof(SecurityIdentifier));
        foreach (FileSystemAccessRule rule in rules)
        {
            if (rule.AccessControlType == AccessControlType.Allow)
                security.RemoveAccessRuleSpecific(rule);
        }
    }
}