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
    ILoggingService log)
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
        var identity = new SecurityIdentifier(sid);
        var groupSids = aclPermission.ResolveAccountGroupSids(sid);

        var (appliedPaths, anyAceAdded) =
            ancestorTraverseGranter.GrantOnPathAndAncestors(normalized, identity, groupSids);

        bool dbEntryIsNew = false;
        if (appliedPaths.Count > 0)
        {
            dbEntryIsNew = dbAccessor.Write(database =>
            {
                var grants = database.GetOrCreateAccount(sid).Grants;
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

            var acct = db.GetAccount(sid);
            var remaining = acct?.Grants
                .Where(e => e.IsTraverseOnly &&
                            !string.Equals(e.Path, normalized, StringComparison.OrdinalIgnoreCase))
                .ToList() ?? [];

            var grantPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (acct != null)
            {
                foreach (var g in acct.Grants.Where(g => !g.IsTraverseOnly && !g.IsDeny))
                {
                    var dir = Directory.Exists(g.Path) ? g.Path : Path.GetDirectoryName(g.Path);
                    if (!string.IsNullOrEmpty(dir))
                        grantPaths.Add(dir);
                }
            }

            return new TraverseRemoveDbState(true, entry.AllAppliedPaths, remaining, grantPaths);
        });

        if (!readResult.Found)
            return false;

        bool pathIsStale = !AclHelper.PathExists(normalized);

        if (updateFileSystem)
        {
            var identity = new SecurityIdentifier(sid);
            var syntheticEntry = new GrantedPathEntry
            {
                Path = normalized, IsTraverseOnly = true, AllAppliedPaths = readResult.AppliedPaths
            };
            ancestorTraverseGranter.RevertForPath(identity, syntheticEntry,
                readResult.RemainingEntries!, readResult.GrantPaths);
        }

        dbAccessor.Write(db =>
        {
            var acct = db.GetAccount(sid);
            var e = acct != null ? FindTraverseEntryInDb(db, sid, normalized) : null;
            if (e != null)
                acct!.Grants.Remove(e);
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
        var identity = new SecurityIdentifier(sid);
        var groupSids = aclPermission.ResolveAccountGroupSids(sid);
        var (appliedPaths, _) =
            ancestorTraverseGranter.GrantOnPathAndAncestors(normalized, identity, groupSids);

        if (appliedPaths.Count > 0)
        {
            dbAccessor.Write(db =>
            {
                var entry = FindTraverseEntryInDb(db, sid, normalized);
                if (entry != null)
                    entry.AllAppliedPaths = appliedPaths;
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
            if (account == null) return (false, (string?)null);

            var tDir = Directory.Exists(normalizedGrantPath)
                ? normalizedGrantPath
                : Path.GetDirectoryName(normalizedGrantPath);

            if (string.IsNullOrEmpty(tDir)) return (false, null);
            if (FindTraverseEntryInDb(db, sid, tDir) == null) return (false, null);

            bool stillNeeded = account.Grants
                .Where(e => !e.IsTraverseOnly && !e.IsDeny &&
                            !string.Equals(e.Path, normalizedGrantPath, StringComparison.OrdinalIgnoreCase))
                .Any(e =>
                {
                    var dir = Directory.Exists(e.Path) ? e.Path : Path.GetDirectoryName(e.Path);
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
        var identity = new SecurityIdentifier(sid);
        var traverseEntries = allGrantsSnapshot.Where(e => e.IsTraverseOnly).ToList();

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
        var account = database.GetAccount(sid);
        return account?.Grants.FirstOrDefault(e =>
            string.Equals(e.Path, normalized, StringComparison.OrdinalIgnoreCase) &&
            e.IsTraverseOnly);
    }

    private void PromoteNearestAncestor(string sid, List<string>? appliedPaths)
    {
        if (appliedPaths == null || appliedPaths.Count == 0)
            return;

        var sidIdentity = new SecurityIdentifier(sid);
        string? promotedPath = null;
        List<string>? remaining = null;

        for (int i = 0; i < appliedPaths.Count; i++)
        {
            var candidate = appliedPaths[i];
            if (!AclHelper.PathExists(candidate))
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

        dbAccessor.Write(db => db.GetOrCreateAccount(sid).Grants.Add(new GrantedPathEntry
        {
            Path = promotedPath,
            IsTraverseOnly = true,
            AllAppliedPaths = remaining ?? new List<string>()
        }));
    }
}
