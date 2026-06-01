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
public class AclAllowModeService(
    ILoggingService log,
    ILocalUserProvider localUserProvider,
    IPathSecurityDescriptorAccessor aclAccessor,
    AppEntryAllowAclRuleProvider allowAclRuleProvider)
    : IAclAllowModeService
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
        var ruleSet = allowAclRuleProvider.BuildAllowModeRuleSet(app, isDirectory);
        foreach (var invalidEntry in ruleSet.InvalidEntries)
            log.Error($"Failed to create allow rule for SID {invalidEntry.Sid}", invalidEntry.Exception);

        Func<FileSystemAccessRule, bool> isManagedAce = rule =>
            rule is { AccessControlType: AccessControlType.Allow, IdentityReference: SecurityIdentifier sid } &&
            ruleSet.ManagedSids.Contains(sid);

        var localUserSids = AclHelper.BuildLocalUserSidSet(localUserProvider.GetLocalUserAccounts());
        bool changed = false;
        aclAccessor.ModifyAclWithFallback(targetPath, security =>
        {
            var denyCleaned = AclHelper.RemoveManagedDenyAces(security, localUserSids);
            var inheritanceBroken = security.AreAccessRulesProtected;

            if (!inheritanceBroken)
                security.SetAccessRuleProtection(true, false);

            changed = AclHelper.ApplyAclDiff(security, ruleSet.Rules, isManagedAce) || !inheritanceBroken || denyCleaned;
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

        aclAccessor.ModifyAclWithFallback(targetPath, security =>
        {
            RemoveAllowManagedAces(security, allowAclRuleProvider.BuildAllowModeRuleSet(app, isDirectory).ManagedSids);
            security.SetAccessRuleProtection(false, false);
            return true;
        });
    }

    public void CleanupAllowModeAces(string targetPath, bool isFolder)
    {
        var exists = isFolder ? Directory.Exists(targetPath) : File.Exists(targetPath);
        if (!exists)
            return;

        try
        {
            var changed = aclAccessor.ModifyAclWithFallback(targetPath, security =>
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

    private static void RemoveAllowManagedAces(
        FileSystemSecurity security,
        IReadOnlySet<SecurityIdentifier> managedSids)
    {
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
