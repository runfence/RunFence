using System.Security.Principal;
using RunFence.Acl.Permissions;
using RunFence.Acl.Traverse;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;

namespace RunFence.Acl;

/// <summary>
/// Pure NTFS+DB traverse operations, fully independent of grant logic and IU sync.
/// All DB access is marshaled to the UI thread via <see cref="UiThreadDatabaseAccessor"/>.
/// NTFS operations remain on the calling thread.
/// </summary>
public class TraverseCoreOperations(
    ITraverseAcl traverseAcl,
    AncestorTraverseGranter ancestorTraverseGranter,
    IAclPermissionService aclPermission,
    UiThreadDatabaseAccessor dbAccessor,
    ILoggingService log,
    IFileSystemPathInfo pathInfo) : ITraverseCoreOperations
{
    /// <summary>
    /// Grants Traverse | ReadAttributes | Synchronize (no inheritance) on <paramref name="path"/>
    /// and every ancestor up to the drive root. Records a <see cref="GrantedPathEntry"/> with
    /// <c>IsTraverseOnly=true</c> in the database.
    /// Returns whether any ACE or DB entry was modified, plus the list of visited paths.
    /// Does NOT sync to the interactive user — that is the orchestrator's concern.
    /// </summary>
    public (bool Modified, List<string> VisitedPaths) AddTraverse(string sid, string path)
    {
        var normalized = Path.GetFullPath(path);
        var aclSid = ResolveTraverseAclSid(sid);
        var identity = new SecurityIdentifier(aclSid);
        var groupSids = aclPermission.ResolveAccountGroupSids(aclSid);
        var effectiveGroupSids = string.Equals(aclSid, sid, StringComparison.OrdinalIgnoreCase)
            ? groupSids
            : aclPermission.ResolveAccountGroupSids(sid);

        var (appliedPaths, anyAceAdded) =
            ancestorTraverseGranter.GrantOnPathAndAncestors(
                normalized,
                identity,
                groupSids,
                effectiveSid: sid,
                effectiveGroupSids: effectiveGroupSids);

        bool dbEntryIsNew = false;
        if (appliedPaths.Count > 0)
        {
            dbEntryIsNew = dbAccessor.Write(database =>
            {
                var grants = GetTraverseStore(database, sid);
                var existing = grants.FirstOrDefault(e =>
                    string.Equals(e.Path, normalized, StringComparison.OrdinalIgnoreCase) &&
                    e.IsTraverseOnly);
                if (existing != null)
                {
                    existing.AllAppliedPaths = appliedPaths;
                    return false;
                }

                grants.Add(new GrantedPathEntry
                {
                    Path = normalized,
                    IsTraverseOnly = true,
                    AllAppliedPaths = appliedPaths
                });
                return true;
            });
        }

        return (anyAceAdded || dbEntryIsNew, appliedPaths);
    }

    /// <summary>
    /// Removes traverse ACEs for <paramref name="sid"/> on <paramref name="path"/>, preserving
    /// paths still needed by other grants or traverse entries. Removes the
    /// <see cref="GrantedPathEntry"/> from the database. When the target path no longer exists,
    /// promotes the nearest valid ancestor with an explicit traverse ACE to a standalone DB entry.
    /// Returns true if a database entry was found and removed.
    /// Does NOT sync to the interactive user — that is the orchestrator's concern.
    /// </summary>
    public bool RemoveTraverse(string sid, string path, bool updateFileSystem)
    {
        var normalized = Path.GetFullPath(path);

        var readResult = dbAccessor.Read(db =>
        {
            var entry = FindTraverseEntryInDb(db, sid, normalized);
            if (entry == null) return default;

            var traverseStore = GetTraverseStoreOrEmpty(db, sid);
            var remaining = traverseStore
                .Where(e => e.IsTraverseOnly &&
                            !string.Equals(e.Path, normalized, StringComparison.OrdinalIgnoreCase))
                .ToList();

            var grantPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var grantOwners = GetGrantOwnersForTraverseCleanup(db, sid);
            foreach (var acct in grantOwners)
            {
                foreach (var g in acct.Grants.Where(g => g is { IsTraverseOnly: false, IsDeny: false }))
                {
                    var dir = pathInfo.DirectoryExists(g.Path) ? g.Path : Path.GetDirectoryName(g.Path);
                    if (!string.IsNullOrEmpty(dir))
                        grantPaths.Add(dir);
                }
            }

            return new TraverseRemoveDbState(true, entry.AllAppliedPaths, remaining, grantPaths);
        });

        if (!readResult.Found)
            return false;

        bool pathIsStale = !pathInfo.DirectoryExists(normalized) && !pathInfo.FileExists(normalized);

        if (updateFileSystem)
        {
            var identity = new SecurityIdentifier(ResolveTraverseAclSid(sid));
            var syntheticEntry = new GrantedPathEntry
            {
                Path = normalized, IsTraverseOnly = true, AllAppliedPaths = readResult.AppliedPaths
            };
            ancestorTraverseGranter.RevertForPath(identity, syntheticEntry,
                readResult.RemainingEntries!, readResult.GrantPaths);
        }

        dbAccessor.Write(db =>
        {
            var store = GetTraverseStoreOrEmpty(db, sid);
            var e = FindTraverseEntryInList(store, normalized);
            if (e != null)
                store.Remove(e);
        });

        if (pathIsStale)
            PromoteNearestAncestor(sid, readResult.AppliedPaths);

        return true;
    }

    /// <summary>
    /// Re-applies traverse ACEs for an existing traverse entry and returns the visited paths.
    /// </summary>
    public List<string> FixTraverse(string sid, string path)
    {
        var normalized = Path.GetFullPath(path);
        var aclSid = ResolveTraverseAclSid(sid);
        var identity = new SecurityIdentifier(aclSid);
        var groupSids = aclPermission.ResolveAccountGroupSids(aclSid);
        var effectiveGroupSids = string.Equals(aclSid, sid, StringComparison.OrdinalIgnoreCase)
            ? groupSids
            : aclPermission.ResolveAccountGroupSids(sid);
        var (appliedPaths, _) =
            ancestorTraverseGranter.GrantOnPathAndAncestors(
                normalized,
                identity,
                groupSids,
                effectiveSid: sid,
                effectiveGroupSids: effectiveGroupSids);

        if (appliedPaths.Count > 0)
        {
            dbAccessor.Write(db =>
            {
                var entry = FindTraverseEntryInDb(db, sid, normalized);
                entry?.AllAppliedPaths = appliedPaths;
            });
        }

        return appliedPaths;
    }

    /// <summary>
    /// Removes the traverse entry for <paramref name="normalizedGrantPath"/>'s directory when no
    /// other allow grant on the same directory still needs it.
    /// </summary>
    public void CleanupOrphanedTraverse(string sid, string normalizedGrantPath)
    {
        var (needsRemoval, traverseDir) = dbAccessor.Read(db =>
        {
            var account = db.GetAccount(sid);
            if (account == null && !UsesSharedContainerTraverse(sid)) return (false, (string?)null);

            var tDir = pathInfo.DirectoryExists(normalizedGrantPath)
                ? normalizedGrantPath
                : Path.GetDirectoryName(normalizedGrantPath);

            if (string.IsNullOrEmpty(tDir) || FindTraverseEntryInDb(db, sid, tDir) == null) return (false, null);

            bool stillNeeded = GetGrantOwnersForTraverseCleanup(db, sid)
                .SelectMany(a => a.Grants.Select(g => (OwnerSid: a.Sid, Grant: g)))
                .Where(e => e.Grant is { IsTraverseOnly: false, IsDeny: false })
                .Where(e => !string.Equals(e.OwnerSid, sid, StringComparison.OrdinalIgnoreCase) ||
                            !string.Equals(e.Grant.Path, normalizedGrantPath, StringComparison.OrdinalIgnoreCase))
                .Any(e =>
                {
                    var dir = pathInfo.DirectoryExists(e.Grant.Path) ? e.Grant.Path : Path.GetDirectoryName(e.Grant.Path);
                    return string.Equals(dir, tDir, StringComparison.OrdinalIgnoreCase);
                });

            return (!stillNeeded, tDir);
        });

        if (needsRemoval && traverseDir != null)
            RemoveTraverse(sid, traverseDir, updateFileSystem: true);
    }

    /// <summary>
    /// Reverts NTFS traverse ACEs for all traverse entries in <paramref name="allGrantsSnapshot"/>
    /// (which is a snapshot of the grants list taken before removal). Errors are logged as warnings.
    /// Used by the orchestrator's <c>RemoveAll</c> when bulk-removing all entries for a SID.
    /// </summary>
    public void RevertAllTraverseAces(string sid, IReadOnlyList<GrantedPathEntry> allGrantsSnapshot)
    {
        var identity = new SecurityIdentifier(ResolveTraverseAclSid(sid));
        var traverseEntries = UsesSharedContainerTraverse(sid)
            ? dbAccessor.Read(db => db.SharedContainerTraverseGrants.Select(e => e.Clone()).ToList())
            : allGrantsSnapshot.Where(e => e.IsTraverseOnly).ToList();

        foreach (var entry in traverseEntries)
        {
            try
            {
                var remainingEntries = traverseEntries
                    .Where(e => !string.Equals(e.Path, entry.Path, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                ancestorTraverseGranter.RevertForPath(identity, entry,
                    remainingEntries, additionalStillNeeded: null);
            }
            catch (Exception ex)
            {
                log.Warn(
                    $"RevertAllTraverseAces: failed to revert traverse on '{entry.Path}' for '{sid}': {ex.Message}");
            }
        }
    }

    private readonly record struct TraverseRemoveDbState(
        bool Found,
        List<string>? AppliedPaths,
        List<GrantedPathEntry>? RemainingEntries,
        HashSet<string>? GrantPaths);

    public static GrantedPathEntry? FindTraverseEntryInDb(AppDatabase database, string sid,
        string normalized)
    {
        return FindTraverseEntryInList(GetTraverseStoreOrEmpty(database, sid), normalized);
    }

    private void PromoteNearestAncestor(string sid, List<string>? appliedPaths)
    {
        if (appliedPaths == null || appliedPaths.Count == 0)
            return;

        var sidIdentity = new SecurityIdentifier(ResolveTraverseAclSid(sid));
        string? promotedPath = null;
        List<string>? remaining = null;

        for (int i = 0; i < appliedPaths.Count; i++)
        {
            var candidate = appliedPaths[i];
            if (!pathInfo.DirectoryExists(candidate) && !pathInfo.FileExists(candidate))
                continue;
            if (!traverseAcl.HasExplicitTraverseAce(candidate, sidIdentity))
                continue;
            promotedPath = candidate;
            remaining = i + 1 < appliedPaths.Count
                ? appliedPaths.Skip(i + 1).ToList()
                : null;
            break;
        }

        if (promotedPath == null)
            return;

        dbAccessor.Write(db => GetTraverseStore(db, sid).Add(new GrantedPathEntry
        {
            Path = promotedPath,
            IsTraverseOnly = true,
            AllAppliedPaths = remaining ?? new List<string>()
        }));
    }

    private static bool UsesSharedContainerTraverse(string sid) => AclHelper.IsSpecificContainerSid(sid);

    private static string ResolveTraverseAclSid(string sid)
    {
        // Specific AppContainer package SID ACEs make ordinary Low Integrity processes lose access
        // to that directory. Container traverse ACEs are shared through ALL APPLICATION PACKAGES
        // so container paths remain reachable without breaking non-container Low IL launches.
        return UsesSharedContainerTraverse(sid) ? AclHelper.AllApplicationPackagesSid : sid;
    }

    private static List<GrantedPathEntry> GetTraverseStore(AppDatabase database, string sid) =>
        UsesSharedContainerTraverse(sid)
            ? database.SharedContainerTraverseGrants
            : database.GetOrCreateAccount(sid).Grants;

    private static List<GrantedPathEntry> GetTraverseStoreOrEmpty(AppDatabase database, string sid) =>
        UsesSharedContainerTraverse(sid)
            ? database.SharedContainerTraverseGrants
            : database.GetAccount(sid)?.Grants ?? [];

    private static IEnumerable<AccountEntry> GetGrantOwnersForTraverseCleanup(AppDatabase database, string sid) =>
        UsesSharedContainerTraverse(sid)
            ? database.Accounts.Where(a => AclHelper.IsSpecificContainerSid(a.Sid))
            : database.GetAccount(sid) is { } account ? [account] : [];

    private static GrantedPathEntry? FindTraverseEntryInList(IEnumerable<GrantedPathEntry> entries, string normalized) =>
        entries.FirstOrDefault(e =>
            string.Equals(e.Path, normalized, StringComparison.OrdinalIgnoreCase) &&
            e.IsTraverseOnly);
}
