using System.Security.Principal;
using RunFence.Acl.Permissions;
using RunFence.Acl.Traverse;
using RunFence.Core.Models;
using RunFence.Launch.Container;
using RunFence.Persistence;

namespace RunFence.Acl.UI;

/// <summary>
/// Resolves nearest-ancestor traverse entries and handles revert/duplicate logic for
/// traverse path entries in the ACL Manager.
/// </summary>
public class TraverseEntryResolver(
    string sid,
    IDatabaseProvider databaseProvider,
    bool isContainer,
    IAppContainerService containerService,
    IUserTraverseService userTraverseService,
    IAclPermissionService aclPermission,
    Dictionary<GrantedPathEntry, GrantedPathEntry> syntheticEntries,
    IGrantConfigTracker grantConfigTracker)
{
    /// <summary>
    /// Finds the nearest valid ancestor for a stale entry (target path missing), creates a
    /// synthetic entry linked to the stale parent, and returns it along with whether all
    /// ancestor paths have effective traverse rights.
    /// </summary>
    public (GrantedPathEntry? Synthetic, bool AllEffective) CreateNearestAncestorEntry(
        GrantedPathEntry staleEntry, SecurityIdentifier sidIdentity, IReadOnlyList<string> groupSids)
    {
        var (path, remaining) = FindNearestValidAncestor(staleEntry.AllAppliedPaths!, sidIdentity, groupSids);
        if (path == null)
            return (null, false);

        var synthetic = new GrantedPathEntry { Path = path, IsTraverseOnly = true, AllAppliedPaths = remaining };
        syntheticEntries[synthetic] = staleEntry;

        bool allEffective = remaining is { Count: > 0 }
                            && remaining.All(p => HasEffectiveTraverse(p, sidIdentity, groupSids));

        return (synthetic, allEffective);
    }

    /// <summary>
    /// Promotes the nearest valid ancestor of a stale entry to a standalone database entry.
    /// Called after the stale entry is removed from the database.
    /// </summary>
    public void PromoteNearestAncestor(GrantedPathEntry staleEntry)
    {
        var sidIdentity = new SecurityIdentifier(sid);
        var groupSids = aclPermission.ResolveAccountGroupSids(sid);
        var (path, remaining) = FindNearestValidAncestor(staleEntry.AllAppliedPaths!, sidIdentity, groupSids);
        if (path == null)
            return;

        databaseProvider.GetDatabase().GetOrCreateAccount(sid).Grants
            .Add(new GrantedPathEntry { Path = path, IsTraverseOnly = true, AllAppliedPaths = remaining });
    }

    /// <summary>
    /// Reverts the traverse ACEs for the given entry.
    /// </summary>
    public void RevertTraverseAces(GrantedPathEntry entry)
    {
        var database = databaseProvider.GetDatabase();
        if (isContainer)
        {
            var container = database.AppContainers.FirstOrDefault(c =>
                string.Equals(containerService.GetSid(c.Name), sid, StringComparison.OrdinalIgnoreCase));
            if (container != null)
                containerService.RevertTraverseAccessForPath(container, entry, database);
        }
        else
        {
            userTraverseService.RevertTraverseAccessForPath(sid, entry, database);
        }
    }

    /// <summary>
    /// Returns true if a duplicate traverse entry for this path exists in a different config.
    /// </summary>
    public bool HasDuplicateTraverseInOtherConfig(GrantedPathEntry entry)
    {
        var entries = databaseProvider.GetDatabase().GetAccount(sid)?.Grants;
        if (entries == null)
            return false;
        var thisConfig = grantConfigTracker.GetGrantConfigPath(sid, entry);
        return entries.Any(e => e != entry &&
                                string.Equals(e.Path, entry.Path, StringComparison.OrdinalIgnoreCase) &&
                                e.IsTraverseOnly &&
                                !string.Equals(grantConfigTracker.GetGrantConfigPath(sid, e), thisConfig,
                                    StringComparison.OrdinalIgnoreCase));
    }

    private static (string? Path, List<string>? Remaining) FindNearestValidAncestor(
        List<string> appliedPaths, SecurityIdentifier sidIdentity, IReadOnlyList<string> groupSids)
    {
        for (int i = 0; i < appliedPaths.Count; i++)
        {
            if (!PathExists(appliedPaths[i]))
                continue;
            if (!TraverseAclNative.HasExplicitTraverseAce(appliedPaths[i], sidIdentity))
                continue;
            return (appliedPaths[i], i + 1 < appliedPaths.Count ? appliedPaths.Skip(i + 1).ToList() : null);
        }

        return (null, null);
    }

    public bool HasEffectiveTraverse(string dirPath, SecurityIdentifier sid, IReadOnlyList<string> groupSids)
        => TraverseRightsHelper.HasEffectiveTraverse(dirPath, sid.Value, groupSids, aclPermission);

    private static bool PathExists(string path) => Directory.Exists(path) || File.Exists(path);
}