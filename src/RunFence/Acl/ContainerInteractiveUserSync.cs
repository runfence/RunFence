using RunFence.Acl.Permissions;
using RunFence.Acl.Traverse;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;

namespace RunFence.Acl;

/// <summary>
/// Synchronizes ACL grants and traverse entries for the interactive user when container
/// grants change. When a container SID gains or loses a grant, the interactive user SID
/// must receive the same grant so the desktop user token can reach the path.
/// </summary>
public class ContainerInteractiveUserSync(
    IGrantCoreOperations grantCore,
    ITraverseCoreOperations traverseCore,
    ITraverseGrantOwnerResolver traverseGrantOwnerResolver,
    IInteractiveUserResolver interactiveUserResolver,
    IAclPermissionService aclPermission,
    UiThreadDatabaseAccessor dbAccessor,
    ILoggingService log,
    IFileSystemPathInfo pathInfo)
    : GrantSyncBase(grantCore, traverseCore, dbAccessor)
{
    /// <summary>
    /// Adds a matching allow grant for the interactive user when the container gains a new
    /// allow grant, but only if the interactive user currently lacks sufficient rights.
    /// </summary>
    public GrantOperationResult SyncAllowGrantToInteractiveUser(string containerSid, string path,
        SavedRightsState rights)
    {
        var iuSid = interactiveUserResolver.GetInteractiveUserSid();
        if (string.IsNullOrEmpty(iuSid))
            return default;

        bool isFolder = pathInfo.DirectoryExists(path);
        var fsRights = GrantRightsMapper.MapAllowRights(rights, isFolder);
        if (!aclPermission.NeedsPermissionGrant(path, iuSid, fsRights))
        {
            var normalized = Path.GetFullPath(path);
            var tracked = DbAccessor.Write(db =>
            {
                var entry = GrantCoreOperations.FindGrantEntryInDb(db, iuSid, normalized, isDeny: false);
                if (entry == null)
                    return false;

                return TryEnsureSourceTracked(entry, containerSid, entryWasCreatedBySync: false);
            });
            return tracked
                ? new GrantOperationResult(GrantAdded: false, TraverseAdded: false, DatabaseModified: true)
                : default;
        }

        return AddGrantForInteractiveUser(containerSid, iuSid, path, rights);
    }

    /// <summary>
    /// Adds or updates the interactive user's matching traverse entry for a container-owned
    /// traverse path. Auto-managed entries track contributing container SIDs in SourceSids;
    /// pre-existing manual IU traverse entries remain untracked.
    /// </summary>
    public bool SyncTraverseToInteractiveUser(string containerSid, string path)
    {
        var iuSid = interactiveUserResolver.GetInteractiveUserSid();
        if (string.IsNullOrEmpty(iuSid))
            return false;

        var normalized = Path.GetFullPath(path);
        var snapshot = DbAccessor.Read(db =>
            traverseGrantOwnerResolver.FindTraverseEntry(db, iuSid, normalized)?.Clone());
        var hadExistingEntry = snapshot != null;
        var modified = false;

        try
        {
            (modified, _) = TraverseCore.AddTraverse(iuSid, normalized);
            var tracked = DbAccessor.Write(db =>
            {
                var entry = traverseGrantOwnerResolver.FindTraverseEntry(db, iuSid, normalized);
                if (entry == null)
                    return false;

                return TryEnsureSourceTracked(entry, containerSid, entryWasCreatedBySync: !hadExistingEntry);
            });

            return modified || tracked;
        }
        catch
        {
            if (modified)
                TryRollbackInteractiveUserTraverseSync(iuSid, normalized, snapshot);
            throw;
        }
    }

    /// <summary>
    /// Removes the interactive user's matching grant for <paramref name="path"/> when
    /// <paramref name="containerSid"/> loses its grant, provided no other container still
    /// needs that path.
    /// </summary>
    public void RevertInteractiveUserGrant(string containerSid, string path)
    {
        var iuSid = interactiveUserResolver.GetInteractiveUserSid();
        if (string.IsNullOrEmpty(iuSid))
            return;

        var revert = DbAccessor.Read(db =>
        {
            var iuEntry = GrantCoreOperations.FindGrantEntryInDb(db, iuSid, path, isDeny: false);
            if (iuEntry == null)
                return new InteractiveUserRevertDecision(false, null, false);

            return BuildGrantRevertDecision(db, iuEntry, containerSid);
        });

        if (!revert.ShouldApply)
            return;

        if (revert.UpdatedSourceSids != null)
        {
            DbAccessor.Write(db =>
            {
                var entry = GrantCoreOperations.FindGrantEntryInDb(db, iuSid, path, isDeny: false);
                if (entry != null)
                {
                    entry.SourceSids = revert.UpdatedSourceSids;
                    entry.OwnerContainerSid = null;
                }
            });
            return;
        }

        GrantCore.RemoveGrant(iuSid, path, isDeny: false, updateFileSystem: true);
        TraverseCore.CleanupOrphanedTraverse(iuSid, path);
    }

    /// <summary>
    /// Removes the interactive user's traverse entry for <paramref name="path"/> when
    /// <paramref name="containerSid"/> loses its traverse, provided no other container still
    /// needs that path.
    /// </summary>
    public void RevertInteractiveUserTraverse(string containerSid, string path)
    {
        var iuSid = interactiveUserResolver.GetInteractiveUserSid();
        if (string.IsNullOrEmpty(iuSid))
            return;

        var normalized = Path.GetFullPath(path);
        var revert = DbAccessor.Read(db =>
        {
            var entry = traverseGrantOwnerResolver.FindTraverseEntry(db, iuSid, normalized);
            if (entry == null)
                return new InteractiveUserRevertDecision(false, null, false);

            if (entry.SourceSids != null)
            {
                if (!entry.SourceSids.Contains(containerSid, StringComparer.OrdinalIgnoreCase))
                    return new InteractiveUserRevertDecision(false, null, false);

                var remaining = entry.SourceSids
                    .Where(s => !string.Equals(s, containerSid, StringComparison.OrdinalIgnoreCase))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                return remaining.Count > 0
                    ? new InteractiveUserRevertDecision(true, remaining, false)
                    : new InteractiveUserRevertDecision(true, null, true);
            }

            return new InteractiveUserRevertDecision(false, null, false);
        });

        if (!revert.ShouldApply)
            return;

        if (revert.UpdatedSourceSids != null)
        {
            DbAccessor.Write(db =>
            {
                var entry = traverseGrantOwnerResolver.FindTraverseEntry(db, iuSid, normalized);
                if (entry != null)
                    entry.SourceSids = revert.UpdatedSourceSids;
            });
            return;
        }

        TraverseCore.RemoveTraverse(iuSid, normalized, updateFileSystem: true);
    }

    /// <summary>
    /// Removes interactive user grants and traverse entries for all paths in
    /// <paramref name="containerGrants"/> when <paramref name="containerSid"/> is fully removed,
    /// provided no other container still needs each path.
    /// </summary>
    public void RevertAllInteractiveUserGrants(string containerSid,
        IReadOnlyList<GrantedPathEntry> containerGrants, bool updateFileSystem)
    {
        var iuSid = interactiveUserResolver.GetInteractiveUserSid();
        if (string.IsNullOrEmpty(iuSid))
            return;

        var pathsToRevert = DbAccessor.Read(db =>
        {
            var result = new List<(string Path, List<string>? RemainingSources, bool RemoveGrant)>();
            foreach (var ce in containerGrants.Where(e => e is { IsDeny: false, IsTraverseOnly: false }))
            {
                var iuEntry = GrantCoreOperations.FindGrantEntryInDb(db, iuSid, ce.Path, isDeny: false);
                if (iuEntry == null)
                    continue;

                var decision = BuildGrantRevertDecision(db, iuEntry, containerSid);
                if (!decision.ShouldApply)
                    continue;

                result.Add((ce.Path, decision.UpdatedSourceSids, decision.RemoveEntry));
            }
            return result;
        });

        foreach (var revert in pathsToRevert)
        {
            try
            {
                if (revert.RemainingSources != null)
                {
                    DbAccessor.Write(db =>
                    {
                        var entry = GrantCoreOperations.FindGrantEntryInDb(db, iuSid, revert.Path, isDeny: false);
                        if (entry != null)
                        {
                            entry.SourceSids = revert.RemainingSources;
                            entry.OwnerContainerSid = null;
                        }
                    });
                    continue;
                }

                if (revert.RemoveGrant)
                {
                    GrantCore.RemoveGrant(iuSid, revert.Path, isDeny: false,
                        updateFileSystem: updateFileSystem);
                    TraverseCore.CleanupOrphanedTraverse(iuSid, revert.Path);
                }
            }
            catch (Exception ex)
            {
                log.Warn(
                    $"RevertAllInteractiveUserGrants: failed to revert grant on '{revert.Path}' for IU '{iuSid}': {ex.Message}");
            }
        }
    }

    private GrantOperationResult AddGrantForInteractiveUser(string containerSid, string iuSid,
        string path, SavedRightsState rights)
    {
        var normalized = Path.GetFullPath(path);
        bool isFolder = pathInfo.DirectoryExists(path);
        var traverseDir = isFolder ? normalized : Path.GetDirectoryName(normalized);
        var snapshot = DbAccessor.Read(db =>
            GrantCoreOperations.FindGrantEntryInDb(db, iuSid, normalized, isDeny: false)?.Clone());
        var traverseSnapshot = string.IsNullOrEmpty(traverseDir)
            ? null
            : DbAccessor.Read(db => traverseGrantOwnerResolver.FindTraverseEntry(db, iuSid, traverseDir)?.Clone());
        var hadExistingEntry = snapshot != null;
        var grantMutated = false;
        GrantAddResult coreResult;
        bool tracked;
        bool traverseMutated = false;

        try
        {
            coreResult = GrantCore.AddGrant(iuSid, path, isDeny: false, rights, ownerSid: null);
            grantMutated = true;
            tracked = DbAccessor.Write(db =>
            {
                var entry = GrantCoreOperations.FindGrantEntryInDb(db, iuSid, normalized, isDeny: false);
                if (entry == null)
                    return false;

                return TryEnsureSourceTracked(entry, containerSid, entryWasCreatedBySync: !hadExistingEntry);
            });

            if (!coreResult.AlreadyExisted)
            {
                bool traverseAdded = false;
                if (!string.IsNullOrEmpty(traverseDir))
                {
                    var (modified, _) = TraverseCore.AddTraverse(iuSid, traverseDir);
                    traverseMutated = modified;
                    traverseAdded = modified;
                }

                return new GrantOperationResult(
                    GrantAdded: true,
                    TraverseAdded: traverseAdded,
                    DatabaseModified: true);
            }

            return new GrantOperationResult(
                GrantAdded: false,
                TraverseAdded: false,
                DatabaseModified: coreResult.DatabaseModified || tracked);
        }
        catch
        {
            if (grantMutated)
                TryRollbackInteractiveUserGrantSync(iuSid, normalized, snapshot, traverseDir, traverseSnapshot, traverseMutated);
            throw;
        }
    }

    private static InteractiveUserRevertDecision BuildGrantRevertDecision(
        AppDatabase database,
        GrantedPathEntry entry,
        string containerSid)
    {
        if (entry.SourceSids != null)
        {
            if (!entry.SourceSids.Contains(containerSid, StringComparer.OrdinalIgnoreCase))
                return new InteractiveUserRevertDecision(false, null, false);

            var remaining = entry.SourceSids
                .Where(s => !string.Equals(s, containerSid, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            return remaining.Count > 0
                ? new InteractiveUserRevertDecision(true, remaining, false)
                : new InteractiveUserRevertDecision(true, null, true);
        }

        if (!string.IsNullOrEmpty(entry.OwnerContainerSid))
        {
            var remaining = GetRemainingContainerGrantSources(database, entry.Path);
            return remaining.Count > 0
                ? new InteractiveUserRevertDecision(true, remaining, false)
                : new InteractiveUserRevertDecision(true, null, true);
        }

        return new InteractiveUserRevertDecision(false, null, false);
    }

    private static List<string> GetRemainingContainerGrantSources(AppDatabase database, string path)
        => database.Accounts
            .Where(a => AclHelper.IsContainerSid(a.Sid))
            .Where(a => a.Grants.Any(e =>
                e is { IsTraverseOnly: false, IsDeny: false } &&
                string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase)))
            .Select(a => a.Sid)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static bool TryEnsureSourceTracked(GrantedPathEntry entry, string containerSid,
        bool entryWasCreatedBySync)
    {
        if (entry.SourceSids != null)
        {
            if (entry.SourceSids.Contains(containerSid, StringComparer.OrdinalIgnoreCase))
                return false;
            entry.SourceSids.Add(containerSid);
            return true;
        }

        if (!string.IsNullOrEmpty(entry.OwnerContainerSid))
        {
            entry.SourceSids = [entry.OwnerContainerSid];
            entry.OwnerContainerSid = null;
            if (!entry.SourceSids.Contains(containerSid, StringComparer.OrdinalIgnoreCase))
                entry.SourceSids.Add(containerSid);
            return true;
        }

        if (entryWasCreatedBySync)
        {
            entry.SourceSids = [containerSid];
            return true;
        }

        return false;
    }

    private void TryRollbackInteractiveUserTraverseSync(
        string iuSid,
        string normalizedPath,
        GrantedPathEntry? snapshot)
    {
        try
        {
            TraverseCore.RemoveTraverse(iuSid, normalizedPath, updateFileSystem: true);
            if (snapshot == null)
                return;

            TraverseCore.TrackTraverse(iuSid, snapshot.Clone());
            if (snapshot.AllAppliedPaths is { Count: > 0 })
                TraverseCore.ApplyTraverseAces(iuSid, snapshot.AllAppliedPaths);
        }
        catch (Exception ex)
        {
            log.Warn(
                $"SyncTraverseToInteractiveUser: failed to rollback IU traverse sync on '{normalizedPath}' for IU '{iuSid}': {ex.Message}");
        }
    }

    private void TryRollbackInteractiveUserGrantSync(
        string iuSid,
        string normalizedPath,
        GrantedPathEntry? snapshot,
        string? traversePath,
        GrantedPathEntry? traverseSnapshot,
        bool traverseMutated)
    {
        try
        {
            if (snapshot == null)
            {
                RemoveGrantWithCleanup(iuSid, normalizedPath, updateFileSystem: true);
            }
            else
            {
                GrantCore.RemoveGrant(iuSid, normalizedPath, isDeny: false, updateFileSystem: true);
                GrantCore.AddGrant(iuSid, normalizedPath, isDeny: false, snapshot.SavedRights, ownerSid: null);
                DbAccessor.Write(db =>
                {
                    var restored = GrantCoreOperations.FindGrantEntryInDb(db, iuSid, normalizedPath, isDeny: false);
                    if (restored != null)
                    {
                        restored.SourceSids = snapshot.SourceSids?.ToList();
                        restored.OwnerContainerSid = snapshot.OwnerContainerSid;
                    }
                });
            }

            if (traverseMutated && !string.IsNullOrEmpty(traversePath))
            {
                TraverseCore.RemoveTraverse(iuSid, traversePath, updateFileSystem: true);
                if (traverseSnapshot != null)
                {
                    TraverseCore.TrackTraverse(iuSid, traverseSnapshot.Clone());
                    if (traverseSnapshot.AllAppliedPaths is { Count: > 0 })
                        TraverseCore.ApplyTraverseAces(iuSid, traverseSnapshot.AllAppliedPaths);
                }
            }
        }
        catch (Exception ex)
        {
            log.Warn(
                $"SyncAllowGrantToInteractiveUser: failed to rollback IU grant sync on '{normalizedPath}' for IU '{iuSid}': {ex.Message}");
        }
    }

    private readonly record struct InteractiveUserRevertDecision(
        bool ShouldApply,
        List<string>? UpdatedSourceSids,
        bool RemoveEntry);

}
