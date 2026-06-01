using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Acl.Traverse;
using RunFence.Persistence;

namespace RunFence.Acl;

public class PersistedTraverseMutationWorkflow(
    ITraverseCoreOperations traverseCore,
    ContainerInteractiveUserSync containerIuSync,
    ITraverseIntentStoreCoordinator traverseIntentStoreCoordinator,
    TraverseGrantStateService traverseGrantStateService,
    GrantRuntimeSnapshotService grantRuntimeSnapshotService,
    Func<IGrantIntentStore> mainGrantIntentStore,
    TraverseIntentStoreMutationService traverseIntentStoreMutationService,
    IGrantIntentStoreSaveService grantIntentStoreSaveService)
{
    internal readonly record struct TraverseMutationPendingSave(
        GrantApplyResult Result,
        IReadOnlyList<IGrantIntentStore> AffectedStores,
        string SavePath);

    private IGrantIntentStore MainGrantIntentStore => mainGrantIntentStore();

    public GrantApplyResult AddTraverse(string accountSid, string path, IGrantIntentStore? store = null)
    {
        var normalized = Path.GetFullPath(path);
        var coveragePaths = traverseCore.CollectCoveragePaths(normalized);
        var pathsNeedingAce = traverseCore.GetPathsNeedingTraverseAce(accountSid, coveragePaths);
        var existingLocations = traverseIntentStoreCoordinator.GetTraverseLocationsForPath(
            accountSid,
            normalized,
            includeManualSharedEntries: true);
        var finalStores = store != null
            ? [store]
            : existingLocations.Count > 0
                ? existingLocations.Select(location => location.Store).Distinct().ToList()
                : [MainGrantIntentStore];
        var affectedStores = existingLocations.Select(location => location.Store)
            .Concat(finalStores)
            .Distinct()
            .ToList();
        var storeOwnerSid = TraverseEntryLookup.ResolveStorageOwnerSid(accountSid);
        var snapshots = traverseGrantStateService.CaptureStoreSnapshots(storeOwnerSid, normalized, affectedStores);
        var runtimeSnapshot = grantRuntimeSnapshotService.CaptureTraverseSnapshot(accountSid, normalized).Entry;
        var primaryConfigPath = grantIntentStoreSaveService.GetPrimaryConfigPath(affectedStores);
        bool storeModified = false;
        var finalStoreSet = finalStores.ToHashSet();

        foreach (var location in existingLocations)
        {
            if (finalStoreSet.Contains(location.Store))
                continue;

            storeModified |= traverseIntentStoreCoordinator.RemoveTraverseEntryFromStore(
                accountSid,
                location.Store,
                location.Entry);
        }

        foreach (var targetStore in finalStores)
        {
            var currentEntry = existingLocations.FirstOrDefault(location =>
                ReferenceEquals(location.Store, targetStore))?.Entry;
            var newEntry = traverseIntentStoreCoordinator.BuildTraverseEntry(
                accountSid,
                normalized,
                coveragePaths,
                currentEntry);
            if (currentEntry == null)
            {
                targetStore.AddEntry(storeOwnerSid, newEntry);
                storeModified = true;
                continue;
            }

            if (!traverseGrantStateService.EntriesEquivalent(currentEntry, newEntry))
            {
                targetStore.ReplaceEntry(storeOwnerSid, currentEntry, newEntry);
                storeModified = true;
            }
        }

        var traverseEntry = traverseIntentStoreCoordinator.BuildTraverseEntry(
            accountSid,
            normalized,
            coveragePaths,
            runtimeSnapshot);
        traverseCore.TrackTraverse(accountSid, traverseEntry);

        if (storeModified)
        {
            try
            {
                grantIntentStoreSaveService.Save(affectedStores, GrantApplyFailureStep.TraverseIntentSave, normalized);
            }
            catch (GrantOperationException ex)
            {
                TryRestoreTrackedRuntimeTraverseEntry(accountSid, normalized, runtimeSnapshot, ex);
                TryRestoreTraverseSnapshots(storeOwnerSid, normalized, snapshots, ex);
                throw;
            }
        }

        List<string> appliedPaths = [];
        try
        {
            if (pathsNeedingAce.Count > 0)
                appliedPaths = traverseCore.ApplyTraverseAces(accountSid, pathsNeedingAce).ToList();
        }
        catch (Exception ex)
        {
            var operationException = new GrantOperationException(
                GrantApplyFailureStep.TraverseAclApply,
                normalized,
                primaryConfigPath,
                ex is TraverseAclApplyException applyEx ? applyEx.InnerException ?? applyEx : ex);
            var rollbackPaths = ex is TraverseAclApplyException applyFailure
                ? applyFailure.AppliedPaths
                : appliedPaths;
            TryRollbackTraverseAcl(
                accountSid,
                rollbackPaths,
                primaryConfigPath,
                operationException,
                new GrantIntentRestoreSnapshot(
                    runtimeSnapshot,
                    [],
                    touchedTraversePaths: rollbackPaths));
            TryRestoreTrackedRuntimeTraverseEntry(accountSid, normalized, runtimeSnapshot, operationException);
            if (storeModified)
                TryRestoreTraverseSnapshots(storeOwnerSid, normalized, snapshots, operationException);
            throw operationException;
        }

        try
        {
            if (appliedPaths.Count > 0)
                traverseCore.VerifyEffectiveTraverse(accountSid, coveragePaths);

            if (AclHelper.IsContainerSid(accountSid))
                containerIuSync.SyncTraverseToInteractiveUser(accountSid, normalized);
        }
        catch (Exception ex)
        {
            var operationException = new GrantOperationException(
                GrantApplyFailureStep.TraverseEffectiveAccessValidation,
                normalized,
                primaryConfigPath,
                ex);
            TryRollbackTraverseAcl(
                accountSid,
                appliedPaths,
                primaryConfigPath,
                operationException,
                new GrantIntentRestoreSnapshot(
                    runtimeSnapshot,
                    [],
                    touchedTraversePaths: appliedPaths));
            TryRestoreTrackedRuntimeTraverseEntry(accountSid, normalized, runtimeSnapshot, operationException);
            if (storeModified)
                TryRestoreTraverseSnapshots(storeOwnerSid, normalized, snapshots, operationException);
            throw operationException;
        }

        return new GrantApplyResult(
            TraverseApplied: appliedPaths.Count > 0,
            DatabaseModified: storeModified,
            DurableSaveCompleted: storeModified);
    }

    public GrantApplyResult RemoveTraverse(string accountSid, string path)
    {
        var normalized = Path.GetFullPath(path);
        var existingLocations = traverseIntentStoreCoordinator.GetTraverseLocationsForPath(
            accountSid,
            normalized,
            includeManualSharedEntries: false);
        if (existingLocations.Count == 0)
            return default;

        var mutation = RemoveTraverseLocationsWithoutSaving(accountSid, normalized, existingLocations);
        var warnings = grantIntentStoreSaveService.SaveWithWarnings(
            mutation.AffectedStores,
            GrantApplyFailureStep.PostTraverseRemoveSave,
            normalized);

        return new GrantApplyResult(
            GrantApplied: mutation.Result.GrantApplied,
            TraverseApplied: mutation.Result.TraverseApplied,
            DatabaseModified: mutation.Result.DatabaseModified,
            DurableSaveCompleted: warnings.Count == 0,
            Warnings: warnings);
    }

    public GrantApplyResult UntrackTraverse(string accountSid, string path)
    {
        var normalized = Path.GetFullPath(path);
        var existingLocations = traverseIntentStoreCoordinator.GetTraverseLocationsForPath(
            accountSid,
            normalized,
            includeManualSharedEntries: false);
        if (existingLocations.Count == 0)
            return default;

        var mutation = UntrackTraverseLocationsWithoutSaving(accountSid, normalized, existingLocations);
        var warnings = grantIntentStoreSaveService.SaveWithWarnings(
            mutation.AffectedStores,
            GrantApplyFailureStep.UntrackTraverseSave,
            normalized);

        return new GrantApplyResult(
            GrantApplied: mutation.Result.GrantApplied,
            TraverseApplied: mutation.Result.TraverseApplied,
            DatabaseModified: mutation.Result.DatabaseModified,
            DurableSaveCompleted: warnings.Count == 0,
            Warnings: warnings);
    }

    public GrantApplyResult FixTraverseAcl(string accountSid, string path)
    {
        var normalized = Path.GetFullPath(path);
        var existingLocation = traverseIntentStoreCoordinator.GetTraverseLocationsForPath(
            accountSid,
            normalized,
            includeManualSharedEntries: false)
            .ToList();
        if (existingLocation.Count == 0)
            return default;

        var storedPaths = traverseGrantStateService.CollectStoredTraversePaths(existingLocation.Select(location => location.Entry));
        var pathsNeedingAce = traverseCore.GetPathsNeedingTraverseAce(accountSid, storedPaths);
        if (pathsNeedingAce.Count == 0)
            return default;

        List<string> appliedPaths = [];
        try
        {
            appliedPaths = traverseCore.ApplyTraverseAces(accountSid, pathsNeedingAce).ToList();
            traverseCore.VerifyEffectiveTraverse(accountSid, storedPaths);
        }
        catch (Exception ex)
        {
            var operationException = new GrantOperationException(
                GrantApplyFailureStep.FixTraverseAclApply,
                normalized,
                grantIntentStoreSaveService.GetPrimaryConfigPath(existingLocation.Select(location => location.Store)),
                ex is TraverseAclApplyException applyEx ? applyEx.InnerException ?? applyEx : ex);
            var rollbackPaths = ex is TraverseAclApplyException applyFailure
                ? applyFailure.AppliedPaths
                : appliedPaths;
            TryRollbackTraverseAcl(
                accountSid,
                rollbackPaths,
                grantIntentStoreSaveService.GetPrimaryConfigPath(existingLocation.Select(location => location.Store)),
                operationException);
            throw operationException;
        }

        return new GrantApplyResult(
            TraverseApplied: appliedPaths.Count > 0);
    }

    internal TraverseMutationPendingSave RemoveAllTraverseWithoutSaving(
        string accountSid,
        IReadOnlyList<IGrantIntentStore>? additionalPrimaryStores)
    {
        var mutation = traverseIntentStoreMutationService.RemoveAllTraverseWithoutSaving(accountSid, additionalPrimaryStores);
        return new TraverseMutationPendingSave(mutation.Result, mutation.AffectedStores, mutation.SavePath);
    }

    internal TraverseMutationPendingSave UntrackAllTraverseWithoutSaving(string accountSid)
    {
        var mutation = traverseIntentStoreMutationService.UntrackAllTraverseWithoutSaving(accountSid);
        return new TraverseMutationPendingSave(mutation.Result, mutation.AffectedStores, mutation.SavePath);
    }

    private TraverseMutationPendingSave RemoveTraverseLocationsWithoutSaving(
        string accountSid,
        string normalized,
        IReadOnlyList<GrantIntentLocation> existingLocations)
    {
        var mutation = traverseIntentStoreMutationService.RemoveTraverseLocationsWithoutSaving(
            accountSid,
            normalized,
            existingLocations);
        return new TraverseMutationPendingSave(mutation.Result, mutation.AffectedStores, mutation.SavePath);
    }

    private TraverseMutationPendingSave UntrackTraverseLocationsWithoutSaving(
        string accountSid,
        string normalized,
        IReadOnlyList<GrantIntentLocation> existingLocations)
    {
        var mutation = traverseIntentStoreMutationService.UntrackTraverseLocationsWithoutSaving(
            accountSid,
            normalized,
            existingLocations);
        return new TraverseMutationPendingSave(mutation.Result, mutation.AffectedStores, mutation.SavePath);
    }

    private void TryRestoreTraverseSnapshots(
        string ownerSid,
        string normalizedPath,
        IReadOnlyList<TraverseGrantStateService.StoreSnapshot> snapshots,
        GrantOperationException operationException)
    {
        try
        {
            traverseGrantStateService.RestoreStoreSnapshots(ownerSid, normalizedPath, snapshots);
            grantIntentStoreSaveService.Save(
                snapshots.Select(snapshot => snapshot.Store),
                GrantApplyFailureStep.RevertIntentSave,
                normalizedPath);
        }
        catch (GrantOperationException ex)
        {
            operationException.AppendCleanupFailure(ex.Step, ex.Path, ex.ConfigPath, ex.Cause);
            operationException.AppendCleanupFailures(ex.CleanupFailures);
        }
        catch (Exception ex)
        {
            operationException.AppendCleanupFailure(
                GrantApplyFailureStep.RevertIntentSave,
                normalizedPath,
                grantIntentStoreSaveService.GetPrimaryConfigPath(snapshots.Select(snapshot => snapshot.Store)),
                ex);
        }
    }

    private void TryRestoreTrackedRuntimeTraverseEntry(
        string sid,
        string normalizedPath,
        GrantedPathEntry? snapshot,
        GrantOperationException operationException)
    {
        try
        {
            grantRuntimeSnapshotService.RestoreTraverseSnapshot(
                new GrantRuntimeEntrySnapshot(
                    sid,
                    normalizedPath,
                    isTraverseOnly: true,
                    isDeny: false,
                    snapshot));
        }
        catch (Exception ex)
        {
            operationException.AppendCleanupFailure(
                GrantApplyFailureStep.RevertIntentSave,
                normalizedPath,
                null,
                ex);
        }
    }

    private void TryRollbackTraverseAcl(
        string sid,
        IReadOnlyList<string> appliedPaths,
        string? primaryConfigPath,
        GrantOperationException operationException,
        GrantIntentRestoreSnapshot? compensationSnapshot = null)
    {
        var rollbackPaths = compensationSnapshot?.TouchedTraversePaths ?? appliedPaths;
        if (rollbackPaths.Count == 0)
            return;

        try
        {
            traverseCore.RemoveTraverseAces(sid, rollbackPaths);
        }
        catch (Exception ex)
        {
            operationException.AppendCleanupFailure(
                GrantApplyFailureStep.TraverseAclRollback,
                rollbackPaths[0],
                primaryConfigPath,
                ex);
        }
    }
}
