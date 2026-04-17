using System.Security.Principal;
using RunFence.Acl.Permissions;
using RunFence.Acl.Traverse;
using RunFence.Core.Models;

namespace RunFence.Acl.UI;

/// <summary>
/// Resolves nearest-ancestor traverse entries for display in the ACL Manager traverse grid.
/// Provides display-only logic: no DB writes, no NTFS ACE modifications.
/// </summary>
public class TraverseEntryResolver(IAclPermissionService aclPermission, ITraverseAcl traverseAcl)
{
    /// <summary>
    /// Finds the nearest valid ancestor for a stale entry (target path missing), creates a
    /// synthetic entry for display, and returns it along with whether all ancestor paths have
    /// effective traverse rights.
    /// </summary>
    public (GrantedPathEntry? Synthetic, bool AllEffective) CreateNearestAncestorEntry(
        GrantedPathEntry staleEntry, SecurityIdentifier sidIdentity, IReadOnlyList<string> groupSids)
    {
        var (path, remaining) = FindNearestValidAncestor(staleEntry.AllAppliedPaths!, sidIdentity);
        if (path == null)
            return (null, false);

        var synthetic = new GrantedPathEntry { Path = path, IsTraverseOnly = true, AllAppliedPaths = remaining };

        bool allEffective = remaining is { Count: > 0 }
                            && remaining.All(p => HasEffectiveTraverse(p, sidIdentity, groupSids));

        return (synthetic, allEffective);
    }

    private (string? Path, List<string>? Remaining) FindNearestValidAncestor(
        List<string> appliedPaths, SecurityIdentifier sidIdentity)
    {
        for (int i = 0; i < appliedPaths.Count; i++)
        {
            if (!AclHelper.PathExists(appliedPaths[i]))
                continue;
            if (!traverseAcl.HasExplicitTraverseAce(appliedPaths[i], sidIdentity))
                continue;
            return (appliedPaths[i], i + 1 < appliedPaths.Count ? appliedPaths.Skip(i + 1).ToList() : null);
        }

        return (null, null);
    }

    public bool HasEffectiveTraverse(string dirPath, SecurityIdentifier sid, IReadOnlyList<string> groupSids)
        => TraverseRightsHelper.HasEffectiveTraverse(dirPath, sid.Value, groupSids, aclPermission);
}
