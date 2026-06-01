using System.Security.AccessControl;
using System.Security.Principal;
using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.Acl;

public sealed class AppEntryManagedAclScanFilter(
    AppEntryAllowAclRuleProvider allowAclRuleProvider,
    IAclDenyModeService denyModeService)
{
    public Func<FileSystemAccessRule, bool> Create(
        string path,
        bool isDirectory,
        IReadOnlyList<AppEntry> apps)
    {
        if (apps.Count == 0)
            return static _ => false;

        var normalizedPath = AclHelper.NormalizePath(path);
        var inheritanceFlags = AclHelper.InheritanceFlagsFor(isDirectory);
        const PropagationFlags propagationFlags = PropagationFlags.None;
        var allowManagedRights = allowAclRuleProvider.BuildAllowManagedRightsBySid(
            normalizedPath,
            isDirectory,
            apps);

        var deniedRightsPerSid = denyModeService.GetDeniedRightsPerSid(
            normalizedPath,
            apps,
            isDirectory);
        var denyManagedRights = deniedRightsPerSid.ToDictionary(
            pair => pair.Key,
            pair => AclRightsHelper.MapDeniedRights(pair.Value),
            StringComparer.OrdinalIgnoreCase);

        return rule =>
        {
            if (rule.IdentityReference is not SecurityIdentifier sid)
                return false;
            if (rule.InheritanceFlags != inheritanceFlags || rule.PropagationFlags != propagationFlags)
                return false;

            if (rule.AccessControlType == AccessControlType.Allow &&
                allowManagedRights.TryGetValue(sid.Value, out var allowRights))
            {
                return (rule.FileSystemRights & ~allowRights) == 0;
            }

            if (rule.AccessControlType == AccessControlType.Deny &&
                denyManagedRights.TryGetValue(sid.Value, out var denyRights))
            {
                return (rule.FileSystemRights & ~denyRights) == 0;
            }

            return false;
        };
    }
}
