using System.Security.AccessControl;
using RunFence.Acl.Permissions;
using RunFence.Acl.Traverse;
using RunFence.Core.Models;
using RunFence.Infrastructure;

namespace RunFence.Acl;

/// <summary>
/// Orchestrator coordinating grant and traverse core operations, interactive-user sync for
/// container SIDs, and confirm callbacks (<see cref="EnsureAccess"/>, <see cref="ResolveDenyConflict"/>).
/// All DB access is marshaled to the UI thread via <see cref="UiThreadDatabaseAccessor"/>
/// (always blocking). The service itself never spawns tasks — callers wrap in
/// Task.Run when non-blocking behavior is needed.
/// </summary>
public class PathGrantService(
    IGrantCoreOperations grantCore,
    ITraverseCoreOperations traverseCore,
    IGrantAceService grantAceService,
    IFileOwnerService fileOwnerService,
    IPathExistenceService pathExistenceService,
    IMandatoryLabelService mandatoryLabelService,
    IInteractiveUserResolver interactiveUserResolver,
    IAclPermissionService aclPermission,
    UiThreadDatabaseAccessor dbAccessor,
    ContainerInteractiveUserSync containerIuSync,
    LowIntegrityGrantSync lowIntegrityGrantSync,
    IGrantSyncService syncService,
    IFileSystemPathInfo pathInfo) : IPathGrantService
{
    // --- Grants ---

    public GrantOperationResult AddGrant(string sid, string path, bool isDeny,
        SavedRightsState? savedRights = null, string? ownerSid = null)
    {
        var normalized = Path.GetFullPath(path);
        var rights = savedRights ?? SavedRightsState.DefaultForMode(isDeny);

        var coreResult = grantCore.AddGrant(sid, normalized, isDeny, rights, ownerSid);

        bool traverseAdded = false;
        if (!isDeny && !coreResult.AlreadyExisted)
        {
            bool isFolder = pathInfo.DirectoryExists(normalized);
            var traverseDir = isFolder ? normalized : Path.GetDirectoryName(normalized);
            if (!string.IsNullOrEmpty(traverseDir))
            {
                var (modified, _) = traverseCore.AddTraverse(sid, traverseDir);
                traverseAdded = modified;
            }

            if (AclHelper.IsContainerSid(sid))
            {
                var iuResult = containerIuSync.SyncAllowGrantToInteractiveUser(sid, normalized, rights);
                traverseAdded |= iuResult.TraverseAdded;
            }

            if (AclHelper.IsLowIntegritySid(sid))
            {
                var sources = dbAccessor.Read(db =>
                    db.Accounts
                        .Where(a => !AclHelper.IsLowIntegritySid(a.Sid) && !AclHelper.IsContainerSid(a.Sid))
                        .Where(a => a.Grants.Any(g => !g.IsTraverseOnly && !g.IsDeny &&
                            string.Equals(g.Path, normalized, StringComparison.OrdinalIgnoreCase)))
                        .Select(a => a.Sid)
                        .ToList());
                if (sources.Count > 0)
                {
                    dbAccessor.Write(db =>
                    {
                        var entry = GrantCoreOperations.FindGrantEntryInDb(db, sid, normalized, isDeny: false);
                        if (entry != null) entry.SourceSids = sources;
                    });
                }
            }

            if (!AclHelper.IsLowIntegritySid(sid) && !AclHelper.IsContainerSid(sid))
            {
                dbAccessor.Write(db =>
                {
                    var lowIlEntry = GrantCoreOperations.FindGrantEntryInDb(
                        db, AclHelper.LowIntegritySid, normalized, isDeny: false);
                    if (lowIlEntry?.SourceSids != null &&
                        !lowIlEntry.SourceSids.Contains(sid, StringComparer.OrdinalIgnoreCase))
                        lowIlEntry.SourceSids.Add(sid);
                });
            }
        }

        if (!isDeny && AclHelper.IsLowIntegritySid(sid) && rights.Write)
        {
            var previousLabel = mandatoryLabelService.ReadMandatoryLabel(normalized);
            dbAccessor.Write(db =>
            {
                var entry = GrantCoreOperations.FindGrantEntryInDb(db, sid, normalized, isDeny: false);
                if (entry != null && entry.PreviousSaclLabel == null)
                    entry.PreviousSaclLabel = previousLabel;
            });
            mandatoryLabelService.ApplyLowIntegrityLabel(normalized);
        }

        return new GrantOperationResult(
            GrantAdded: !coreResult.AlreadyExisted,
            TraverseAdded: traverseAdded,
            DatabaseModified: coreResult.DatabaseModified || traverseAdded);
    }

    public GrantOperationResult EnsureAccess(string sid, string path, SavedRightsState savedRights,
        Func<string, string, bool>? confirm = null, bool unelevated = false)
    {
        var normalized = Path.GetFullPath(path);
        bool isFolder = pathInfo.DirectoryExists(normalized);
        bool pathExists = isFolder || pathInfo.FileExists(normalized);

        // For paths inaccessible to standard APIs (explicit deny on admins) but reachable via
        // backup/restore privilege, fall back to IAclAccessor which uses FILE_FLAG_BACKUP_SEMANTICS.
        if (!pathExists && pathExistenceService.PathExists(normalized, out bool aclIsFolder))
        {
            pathExists = true;
            if (aclIsFolder)
                isFolder = true;
        }

        var requiredRights = GrantRightsMapper.MapAllowRights(savedRights, isFolder);

        if (pathExists &&
            AclHelper.IsSpecificContainerSid(sid) &&
            !aclPermission.NeedsPermissionGrant(normalized, AclHelper.AllApplicationPackagesSid, requiredRights, unelevated))
        {
            return new GrantOperationResult(GrantAdded: false, TraverseAdded: false, DatabaseModified: false);
        }

        ResolveDenyConflict(sid, normalized, savedRights, isFolder, confirm);

        var dbState = dbAccessor.Read(db =>
        {
            var existing = GrantCoreOperations.FindGrantEntryInDb(db, sid, normalized, isDeny: false);
            var tRights = MergeAllowRights(existing?.SavedRights, savedRights);
            var tFsRights = GrantRightsMapper.MapAllowRights(tRights, isFolder);
            var traverseDir = isFolder ? normalized : Path.GetDirectoryName(normalized);
            bool hasTrav = !string.IsNullOrEmpty(traverseDir) && existing != null
                && TraverseCoreOperations.FindTraverseEntryInDb(db, sid, traverseDir) != null;
            return new
            {
                ExistingRights = existing?.SavedRights,
                HasExisting = existing != null,
                TargetRights = tRights,
                TargetFsRights = tFsRights,
                HasTraverseEntry = hasTrav
            };
        });

        bool needsFix = false;
        bool traverseNeedsFix = false;
        if (dbState.HasExisting && pathExists)
        {
            var groupSids = aclPermission.ResolveAccountGroupSids(sid);
            var state = grantAceService.ReadGrantState(normalized, sid, groupSids);
            if (state.DirectAllowAceCount == 0)
            {
                needsFix = true;
            }
            else
            {
                var diskRights = GrantRightsMapper.MapAllowRights(BuildSavedRightsFromState(state), isFolder);
                if (diskRights != dbState.TargetFsRights)
                    needsFix = true;
            }

            var traverseDir = isFolder ? normalized : Path.GetDirectoryName(normalized);
            if (!string.IsNullOrEmpty(traverseDir))
            {
                if (dbState.HasTraverseEntry &&
                    !TraverseRightsHelper.HasEffectiveTraverseForGrantSid(traverseDir, sid, groupSids, aclPermission, pathInfo))
                    traverseNeedsFix = true;
            }
        }

        bool grantNeeded = needsFix ||
            (pathExists && aclPermission.NeedsPermissionGrant(normalized, sid, requiredRights, unelevated));

        if (!grantNeeded && !traverseNeedsFix)
            return new GrantOperationResult(GrantAdded: false, TraverseAdded: false, DatabaseModified: false);

        if (traverseNeedsFix && !grantNeeded)
        {
            var traverseDir = isFolder ? normalized : Path.GetDirectoryName(normalized);
            if (!string.IsNullOrEmpty(traverseDir))
                traverseCore.AddTraverse(sid, traverseDir);
            return new GrantOperationResult(GrantAdded: false, TraverseAdded: true, DatabaseModified: true);
        }

        if (confirm != null)
        {
            bool proceed = confirm(normalized, sid);
            if (!proceed)
                throw new OperationCanceledException($"User declined to grant access to '{normalized}'.");
        }

        var result = AddGrant(sid, normalized, isDeny: false, dbState.TargetRights);

        if (traverseNeedsFix)
        {
            var traverseDir = isFolder ? normalized : Path.GetDirectoryName(normalized);
            if (!string.IsNullOrEmpty(traverseDir))
            {
                var (traverseModified, _) = traverseCore.AddTraverse(sid, traverseDir);
                if (traverseModified)
                    result = result with { TraverseAdded = true, DatabaseModified = true };
            }
        }

        if (pathExists)
        {
            var appliedFsRights = GrantRightsMapper.MapAllowRights(dbState.TargetRights, isFolder);
            if (aclPermission.NeedsPermissionGrant(normalized, sid, appliedFsRights, unelevated))
                throw new InvalidOperationException(
                    $"Grant applied but effective access is still insufficient on '{normalized}'. " +
                    $"A parent deny entry may be blocking access.");
        }

        return result;
    }

    public GrantOperationResult EnsureAccess(string sid, string path, FileSystemRights rights,
        Func<string, string, bool>? confirm = null, bool unelevated = false)
    {
        var normalized = Path.GetFullPath(path);
        bool isFolder = pathInfo.DirectoryExists(normalized);
        var savedRights = GrantRightsMapper.FromRights(rights, isFolder, isDeny: false);
        return EnsureAccess(sid, normalized, savedRights, confirm, unelevated);
    }

    public bool RemoveGrant(string sid, string path, bool isDeny, bool updateFileSystem)
    {
        var normalized = Path.GetFullPath(path);

        bool hadWrite = false;
        string? previousSaclLabel = null;
        if (!isDeny && AclHelper.IsLowIntegritySid(sid))
        {
            dbAccessor.Read(db =>
            {
                var entry = GrantCoreOperations.FindGrantEntryInDb(db, sid, normalized, isDeny: false);
                hadWrite = entry?.SavedRights?.Write == true;
                previousSaclLabel = entry?.PreviousSaclLabel;
            });
        }

        var coreResult = grantCore.RemoveGrant(sid, normalized, isDeny, updateFileSystem);
        if (!coreResult.Found)
            return false;

        if (!isDeny)
        {
            traverseCore.CleanupOrphanedTraverse(sid, normalized);

            if (AclHelper.IsContainerSid(sid))
                containerIuSync.RevertInteractiveUserGrant(sid, normalized);
            else if (AclHelper.IsLowIntegritySid(sid))
            {
                if (updateFileSystem && hadWrite)
                    mandatoryLabelService.RestoreMandatoryLabel(normalized, previousSaclLabel);
            }
            else
                lowIntegrityGrantSync.RevertSource(sid, normalized, updateFileSystem);
        }

        return true;
    }

    public GrantOperationResult UpdateGrant(string sid, string path, bool isDeny,
        SavedRightsState savedRights, string? ownerSid = null)
    {
        var normalized = Path.GetFullPath(path);

        bool oldHadWrite = false;
        string? oldPreviousSaclLabel = null;
        if (!isDeny && AclHelper.IsLowIntegritySid(sid))
        {
            dbAccessor.Read(db =>
            {
                var entry = GrantCoreOperations.FindGrantEntryInDb(db, sid, normalized, isDeny: false);
                oldHadWrite = entry?.SavedRights?.Write == true;
                oldPreviousSaclLabel = entry?.PreviousSaclLabel;
            });
        }

        grantCore.UpdateGrant(sid, normalized, isDeny, savedRights, ownerSid);

        if (!isDeny && AclHelper.IsLowIntegritySid(sid))
        {
            bool newHasWrite = savedRights.Write;
            if (!oldHadWrite && newHasWrite)
            {
                var previousLabel = mandatoryLabelService.ReadMandatoryLabel(normalized);
                dbAccessor.Write(db =>
                {
                    var entry = GrantCoreOperations.FindGrantEntryInDb(db, sid, normalized, isDeny: false);
                    if (entry != null && entry.PreviousSaclLabel == null)
                        entry.PreviousSaclLabel = previousLabel;
                });
                mandatoryLabelService.ApplyLowIntegrityLabel(normalized);
            }
            else if (oldHadWrite && !newHasWrite)
            {
                dbAccessor.Write(db =>
                {
                    var entry = GrantCoreOperations.FindGrantEntryInDb(db, sid, normalized, isDeny: false);
                    if (entry != null) entry.PreviousSaclLabel = null;
                });
                mandatoryLabelService.RestoreMandatoryLabel(normalized, oldPreviousSaclLabel);
            }
        }

        return new GrantOperationResult(GrantAdded: false, TraverseAdded: false, DatabaseModified: true);
    }

    public bool UpdateFromPath(string path, string? sid = null)
        => syncService.UpdateFromPath(path, sid);

    // --- Traverse ---

    public (bool Modified, List<string> VisitedPaths) AddTraverse(string sid, string path)
    {
        var (modified, visitedPaths) = traverseCore.AddTraverse(sid, path);

        bool iuModified = false;
        if (AclHelper.IsContainerSid(sid))
        {
            var iuSid = interactiveUserResolver.GetInteractiveUserSid();
            if (!string.IsNullOrEmpty(iuSid))
            {
                var (iuMod, _) = traverseCore.AddTraverse(iuSid, Path.GetFullPath(path));
                iuModified = iuMod;
            }
        }

        return (modified || iuModified, visitedPaths);
    }

    public bool RemoveTraverse(string sid, string path, bool updateFileSystem)
    {
        bool removed = traverseCore.RemoveTraverse(sid, path, updateFileSystem);

        if (removed && AclHelper.IsContainerSid(sid))
            containerIuSync.RevertInteractiveUserTraverse(sid, Path.GetFullPath(path));

        return removed;
    }

    public void CleanupOrphanedTraverse(string sid, string path)
        => traverseCore.CleanupOrphanedTraverse(sid, Path.GetFullPath(path));

    public List<string> FixTraverse(string sid, string path)
        => traverseCore.FixTraverse(sid, path);

    // --- Bulk / Query ---

    public void RemoveAll(string sid, bool updateFileSystem)
    {
        var allGrants = dbAccessor.Read(db =>
        {
            var account = db.GetAccount(sid);
            return account?.Grants.Select(e => e.Clone()).ToList() ?? [];
        });

        if (allGrants.Count == 0)
            return;

        var removedGrants = grantCore.RemoveAllGrants(sid, updateFileSystem);

        if (updateFileSystem)
        {
            if (AclHelper.IsSpecificContainerSid(sid))
            {
                foreach (var grant in removedGrants.Where(e => !e.IsDeny && !e.IsTraverseOnly))
                    traverseCore.CleanupOrphanedTraverse(sid, grant.Path);
            }
            else
            {
                traverseCore.RevertAllTraverseAces(sid, allGrants);
            }
        }

        if (AclHelper.IsContainerSid(sid))
            containerIuSync.RevertAllInteractiveUserGrants(sid, removedGrants, updateFileSystem);
        else if (AclHelper.IsLowIntegritySid(sid))
        {
            if (updateFileSystem)
            {
                foreach (var grant in allGrants.Where(e => !e.IsDeny && e.SavedRights?.Write == true))
                    mandatoryLabelService.RestoreMandatoryLabel(grant.Path, grant.PreviousSaclLabel);
            }
        }
        else
            lowIntegrityGrantSync.RevertAllSources(sid, updateFileSystem);

        dbAccessor.Write(db => db.GetAccount(sid)?.Grants.Clear());
    }

    public void FixGrant(string sid, string path, bool isDeny)
        => grantCore.FixGrant(sid, path, isDeny);

    public GrantRightsState ReadGrantState(string path, string sid, IReadOnlyList<string> groupSids)
        => grantAceService.ReadGrantState(path, sid, groupSids);

    public PathAclStatus CheckGrantStatus(string path, string sid, bool isDeny)
        => grantAceService.CheckGrantStatus(path, sid, isDeny);

    public void ValidateGrant(string sid, string path, bool isDeny)
        => grantCore.ValidateGrant(sid, path, isDeny);

    // --- Ownership ---

    public void ChangeOwner(string path, string sid, bool recursive)
        => fileOwnerService.ChangeOwner(path, sid, recursive);

    public void ResetOwner(string path, bool recursive)
        => fileOwnerService.ResetOwner(path, recursive);

    // --- Private helpers ---

    private void ResolveDenyConflict(string sid, string normalized, SavedRightsState requestedAllow,
        bool isFolder, Func<string, string, bool>? confirm)
    {
        var denyState = dbAccessor.Read(db =>
        {
            var entry = GrantCoreOperations.FindGrantEntryInDb(db, sid, normalized, isDeny: true);
            return entry != null
                ? entry.SavedRights ?? SavedRightsState.DefaultForMode(isDeny: true)
                : null;
        });

        if (denyState == null)
            return;

        var requestedFsRights = GrantRightsMapper.MapAllowRights(requestedAllow, isFolder);
        var denyFsRights = GrantRightsMapper.MapDenyRights(denyState, isFolder);

        if ((denyFsRights & requestedFsRights) == 0)
            return;

        if (confirm == null)
            throw new InvalidOperationException(
                $"Deny entry blocks requested access on '{normalized}'. Remove the deny entry first.");

        bool proceed = confirm(normalized, sid);
        if (!proceed)
            throw new OperationCanceledException($"User declined to resolve deny conflict on '{normalized}'.");

        bool newDenyRead = denyState.Read && !requestedAllow.Read;
        bool newDenyExecute = denyState.Execute && !requestedAllow.Execute;

        if (!newDenyRead && !newDenyExecute)
        {
            RemoveGrant(sid, normalized, isDeny: true, updateFileSystem: true);
        }
        else
        {
            UpdateGrant(sid, normalized, isDeny: true,
                denyState with { Read = newDenyRead, Execute = newDenyExecute });
        }
    }

    private static SavedRightsState MergeAllowRights(SavedRightsState? existing,
        SavedRightsState requested)
    {
        if (existing == null)
            return requested;
        return new SavedRightsState(
            Execute: existing.Execute || requested.Execute,
            Write: existing.Write || requested.Write,
            Read: true,
            Special: existing.Special || requested.Special,
            Own: existing.Own || requested.Own);
    }

    private static SavedRightsState BuildSavedRightsFromState(GrantRightsState state)
        => new(
            Execute: state.AllowExecute == RightCheckState.Checked,
            Write: state.AllowWrite == RightCheckState.Checked,
            Read: true,
            Special: state.AllowSpecial == RightCheckState.Checked,
            Own: state.IsAccountOwner == RightCheckState.Checked);
}
