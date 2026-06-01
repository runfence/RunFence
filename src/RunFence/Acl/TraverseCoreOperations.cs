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
internal sealed class TraverseAclApplyException(IReadOnlyList<string> appliedPaths, Exception innerException)
    : Exception(innerException.Message, innerException)
{
    public IReadOnlyList<string> AppliedPaths { get; } = appliedPaths.ToList();
}

public class TraverseCoreOperations(
    ITraverseAcl traverseAcl,
    AncestorTraverseGranter ancestorTraverseGranter,
    IAclPermissionService aclPermission,
    UiThreadDatabaseAccessor dbAccessor,
    ILoggingService log,
    IFileSystemPathInfo pathInfo,
    ITraverseGrantOwnerResolver ownerResolver) : ITraverseCoreOperations
{
    public IReadOnlyList<string> CollectCoveragePaths(string path)
    {
        var normalized = Path.GetFullPath(path);
        var current = new DirectoryInfo(normalized);
        var coveragePaths = new List<string>();

        while (current != null)
        {
            if (pathInfo.DirectoryExists(current.FullName))
                coveragePaths.Add(current.FullName);

            current = current.Parent;
        }

        return coveragePaths;
    }

    public IReadOnlyList<string> GetPathsNeedingTraverseAce(string sid, IReadOnlyList<string> coveragePaths, bool unelevated = true)
    {
        var effectiveGroupSids = aclPermission.ResolveAccountGroupSids(sid);
        var pathsNeedingAce = new List<string>();

        foreach (var coveragePath in coveragePaths)
        {
            if (!TraverseRightsHelper.HasEffectiveTraverseForGrantSid(
                    coveragePath,
                    sid,
                    effectiveGroupSids,
                    aclPermission,
                    pathInfo,
                    unelevated))
            {
                pathsNeedingAce.Add(coveragePath);
            }
        }

        return pathsNeedingAce;
    }

    public bool TrackTraverse(string sid, GrantedPathEntry entry)
    {
        var normalized = Path.GetFullPath(entry.Path);

        return dbAccessor.Write(database =>
        {
            var grants = ownerResolver.GetOrCreateTraverseStore(database, sid);
            var existing = FindTraverseEntryForMutation(
                grants,
                sid,
                normalized,
                sourceTrackedEntry: entry.SourceSids != null);
            if (existing != null)
            {
                var changed = !StringListsEquivalent(existing.AllAppliedPaths, entry.AllAppliedPaths);
                existing.AllAppliedPaths = entry.AllAppliedPaths?.ToList();

                if (!ownerResolver.UsesSharedContainerTraverse(sid))
                    return changed;

                if (existing.SourceSids == null || entry.SourceSids == null)
                    return changed;

                foreach (var sourceSid in entry.SourceSids)
                {
                    if (!existing.SourceSids.Contains(sourceSid, StringComparer.OrdinalIgnoreCase))
                    {
                        existing.SourceSids.Add(sourceSid);
                        changed = true;
                    }
                }

                return changed;
            }

            var clone = entry.Clone();
            clone.Path = normalized;
            clone.IsTraverseOnly = true;
            grants.Add(clone);
            return true;
        });
    }

    public IReadOnlyList<string> ApplyTraverseAces(string sid, IReadOnlyList<string> paths)
    {
        var identity = new SecurityIdentifier(ownerResolver.ResolveAclSid(sid));
        var appliedPathSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var orderedPaths = paths
            .OrderBy(path => path.Count(static c => c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar))
            .ThenBy(path => path.Length)
            .ToList();

        try
        {
            foreach (var path in orderedPaths)
            {
                if (!pathInfo.DirectoryExists(path))
                    continue;

                traverseAcl.AddAllowAce(path, identity);
                appliedPathSet.Add(path);
            }
        }
        catch (Exception ex)
        {
            throw new TraverseAclApplyException(
                paths.Where(appliedPathSet.Contains).ToList(),
                ex);
        }

        return paths.Where(appliedPathSet.Contains).ToList();
    }

    public void RemoveTraverseAces(string sid, IReadOnlyList<string> paths)
    {
        var identity = new SecurityIdentifier(ownerResolver.ResolveAclSid(sid));

        foreach (var path in paths)
        {
            if (!pathInfo.DirectoryExists(path))
                continue;

            if (!traverseAcl.HasExplicitTraverseAceOrThrow(path, identity))
                continue;

            traverseAcl.RemoveTraverseOnlyAce(path, identity);
        }
    }

    public void VerifyEffectiveTraverse(string sid, IReadOnlyList<string> paths, bool unelevated = true)
    {
        var effectiveGroupSids = aclPermission.ResolveAccountGroupSids(sid);

        foreach (var path in paths)
        {
            if (!pathInfo.DirectoryExists(path))
                continue;

            if (TraverseRightsHelper.HasEffectiveTraverseForGrantSid(
                    path,
                    sid,
                    effectiveGroupSids,
                    aclPermission,
                    pathInfo,
                    unelevated))
            {
                continue;
            }

            throw new InvalidOperationException(
                $"Traverse access is still insufficient on '{path}'.");
        }
    }

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
        var aclSid = ownerResolver.ResolveAclSid(sid);
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
            try
            {
                dbEntryIsNew = dbAccessor.Write(database =>
                {
                    var grants = ownerResolver.GetOrCreateTraverseStore(database, sid);
                    var existing = FindTraverseEntryForMutation(
                        grants,
                        sid,
                        normalized,
                        sourceTrackedEntry: true);
                    if (existing != null)
                    {
                        existing.AllAppliedPaths = appliedPaths;
                        if (!ownerResolver.UsesSharedContainerTraverse(sid) ||
                            existing.SourceSids == null ||
                            existing.SourceSids.Contains(sid, StringComparer.OrdinalIgnoreCase))
                            return false;
                        existing.SourceSids.Add(sid);
                        return true;
                    }

                    var newEntry = new GrantedPathEntry
                    {
                        Path = normalized,
                        IsTraverseOnly = true,
                        AllAppliedPaths = appliedPaths
                    };
                    if (ownerResolver.UsesSharedContainerTraverse(sid))
                        newEntry.SourceSids = [sid];
                    grants.Add(newEntry);
                    return true;
                });
            }
            catch
            {
                if (anyAceAdded)
                {
                    ancestorTraverseGranter.RevertForPath(
                        identity,
                        new GrantedPathEntry
                        {
                            Path = normalized,
                            IsTraverseOnly = true,
                            AllAppliedPaths = appliedPaths
                        },
                        [],
                        additionalStillNeeded: null);
                }

                throw;
            }
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
            var entry = ownerResolver.FindTraverseEntry(db, sid, normalized);
            if (entry == null) return default;

            var sourceSids = entry.SourceSids?.ToList();
            bool shouldRemoveEntry = true;
            if (ownerResolver.UsesSharedContainerTraverse(sid))
            {
                if (sourceSids == null ||
                    !sourceSids.Contains(sid, StringComparer.OrdinalIgnoreCase))
                    return default;

                sourceSids.RemoveAll(s => string.Equals(s, sid, StringComparison.OrdinalIgnoreCase));
                shouldRemoveEntry = sourceSids.Count == 0;
            }

            if (!shouldRemoveEntry)
                return new TraverseRemoveDbState(true, entry.AllAppliedPaths, null, null, false, sourceSids);

            var traverseStore = ownerResolver.GetTraverseStoreOrEmpty(db, sid);
            var remaining = traverseStore
                .Where(e => e.IsTraverseOnly &&
                            !string.Equals(e.Path, normalized, StringComparison.OrdinalIgnoreCase))
                .ToList();

            var grantPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var grantOwners = ownerResolver.GetGrantOwnersForTraverseCleanup(db, sid);
            foreach (var acct in grantOwners)
            {
                foreach (var g in acct.Grants.Where(g => g is { IsTraverseOnly: false, IsDeny: false }))
                {
                    var dir = pathInfo.DirectoryExists(g.Path) ? g.Path : Path.GetDirectoryName(g.Path);
                    if (!string.IsNullOrEmpty(dir))
                        grantPaths.Add(dir);
                }
            }

            return new TraverseRemoveDbState(true, entry.AllAppliedPaths, remaining, grantPaths, true, sourceSids);
        });

        if (!readResult.Found)
            return false;

        if (!readResult.ShouldRemoveEntry)
        {
            dbAccessor.Write(db =>
            {
                var store = ownerResolver.GetTraverseStoreOrEmpty(db, sid);
                var e = ownerResolver.FindTraverseEntry(db, sid, normalized);
                if (e != null)
                    e.SourceSids = readResult.UpdatedSourceSids;
            });
            return true;
        }

        bool pathIsStale = !pathInfo.DirectoryExists(normalized) && !pathInfo.FileExists(normalized);

        if (updateFileSystem)
        {
            var identity = new SecurityIdentifier(ownerResolver.ResolveAclSid(sid));
            var syntheticEntry = new GrantedPathEntry
            {
                Path = normalized, IsTraverseOnly = true, AllAppliedPaths = readResult.AppliedPaths
            };
            ancestorTraverseGranter.RevertForPath(identity, syntheticEntry,
                readResult.RemainingEntries!, readResult.GrantPaths);
        }

        dbAccessor.Write(db =>
        {
            var store = ownerResolver.GetTraverseStoreOrEmpty(db, sid);
            var e = ownerResolver.FindTraverseEntry(db, sid, normalized);
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
        var aclSid = ownerResolver.ResolveAclSid(sid);
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
                var entry = ownerResolver.FindTraverseEntry(db, sid, normalized);
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
            if (account == null && !ownerResolver.UsesSharedContainerTraverse(sid)) return (false, (string?)null);

            var tDir = pathInfo.DirectoryExists(normalizedGrantPath)
                ? normalizedGrantPath
                : Path.GetDirectoryName(normalizedGrantPath);

            if (string.IsNullOrEmpty(tDir) || ownerResolver.FindTraverseEntry(db, sid, tDir) == null) return (false, null);

            bool stillNeeded = ownerResolver.GetGrantOwnersForTraverseCleanup(db, sid)
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
        var identity = new SecurityIdentifier(ownerResolver.ResolveAclSid(sid));
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
        HashSet<string>? GrantPaths,
        bool ShouldRemoveEntry,
        List<string>? UpdatedSourceSids);

    private void PromoteNearestAncestor(string sid, List<string>? appliedPaths)
    {
        if (appliedPaths == null || appliedPaths.Count == 0)
            return;

        var sidIdentity = new SecurityIdentifier(ownerResolver.ResolveAclSid(sid));
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

        dbAccessor.Write(db =>
        {
            var entry = new GrantedPathEntry
            {
                Path = promotedPath,
                IsTraverseOnly = true,
                AllAppliedPaths = remaining ?? new List<string>()
            };
            if (ownerResolver.UsesSharedContainerTraverse(sid))
                entry.SourceSids = [sid];

            ownerResolver.GetOrCreateTraverseStore(db, sid).Add(entry);
        });
    }

    private GrantedPathEntry? FindTraverseEntryForMutation(
        IEnumerable<GrantedPathEntry> entries,
        string sid,
        string normalized,
        bool sourceTrackedEntry)
    {
        if (!ownerResolver.UsesSharedContainerTraverse(sid))
        {
            return entries.FirstOrDefault(e =>
                string.Equals(e.Path, normalized, StringComparison.OrdinalIgnoreCase) &&
                e.IsTraverseOnly);
        }

        return entries.FirstOrDefault(e =>
            string.Equals(e.Path, normalized, StringComparison.OrdinalIgnoreCase) &&
            e.IsTraverseOnly &&
            (sourceTrackedEntry ? e.SourceSids != null : e.SourceSids == null));
    }

    private static bool StringListsEquivalent(IReadOnlyList<string>? left, IReadOnlyList<string>? right)
    {
        if (left == null || right == null)
            return left == right;

        return left.SequenceEqual(right, StringComparer.OrdinalIgnoreCase);
    }

}
