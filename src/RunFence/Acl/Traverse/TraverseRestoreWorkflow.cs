using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Persistence;
using System.Security.Principal;

namespace RunFence.Acl.Traverse;

public class TraverseRestoreWorkflow(
    ITraverseCoreOperations traverseCore,
    UiThreadDatabaseAccessor dbAccessor,
    ContainerInteractiveUserSync containerIuSync,
    IFileSystemPathInfo pathInfo,
    ITraverseAcl traverseAcl,
    ITraverseGrantOwnerResolver traverseGrantOwnerResolver,
    ITraverseIntentStoreCoordinator traverseIntentStoreCoordinator,
    TraverseGrantStateService traverseGrantStateService,
    Func<IGrantIntentStoreProvider> grantIntentStoreProvider,
    IGrantIntentStoreSaveService grantIntentStoreSaveService)
{
    private enum GrantMutationOrder
    {
        SaveThenApply,
        ApplyThenSave,
        RemoveSaveAdd
    }

    private IGrantIntentStoreProvider GrantIntentStoreProvider => grantIntentStoreProvider();

    public GrantApplyResult Restore(
        string accountSid,
        string normalizedPath,
        GrantIntentRestoreSnapshot previousState)
    {
        if (previousState.RuntimeEntry == null && previousState.Locations.Count == 0)
            return RemoveTraverse(accountSid, normalizedPath);

        var previousEntry = previousState.RuntimeEntry ?? previousState.Locations[0].Entry;

        if (!previousEntry.IsTraverseOnly || previousEntry.IsDeny)
            throw new InvalidOperationException("RestoreTraverse only supports allow traverse entries.");

        var restoredEntry = previousEntry.Clone();
        restoredEntry.Path = normalizedPath;
        restoredEntry.IsTraverseOnly = true;
        restoredEntry.IsDeny = false;

        var existingLocations = traverseIntentStoreCoordinator.GetTraverseLocationsForPath(
            accountSid,
            normalizedPath,
            includeManualSharedEntries: true);
        var finalStores = previousState.Locations
            .Select(location => GrantIntentStoreProvider.ResolveStore(location.ConfigPath))
            .Distinct()
            .ToList();
        var affectedStores = existingLocations.Select(location => location.Store)
            .Concat(finalStores)
            .Distinct()
            .ToList();
        var storeOwnerSid = traverseIntentStoreCoordinator.ResolveStorageOwnerSid(accountSid);
        var snapshots = traverseGrantStateService.CaptureStoreSnapshots(storeOwnerSid, normalizedPath, affectedStores);
        var runtimeSnapshot = dbAccessor.Read(db =>
            FindRuntimeTraverseEntry(db, accountSid, normalizedPath, includeManualSharedEntries: true)?.Clone());
        var primaryConfigPath = grantIntentStoreSaveService.GetPrimaryConfigPath(affectedStores);
        bool storeModified = RestoreTraverseStoresToExactLocations(
            accountSid,
            existingLocations,
            previousState.Locations,
            mutate: false);

        var currentTrackedEntries = existingLocations.Select(location => location.Entry).ToList();
        if (currentTrackedEntries.Count == 0 && runtimeSnapshot != null)
            currentTrackedEntries.Add(runtimeSnapshot);

        var currentPaths = traverseGrantStateService.CollectStoredTraversePaths(currentTrackedEntries);
        var restoredPaths = traverseGrantStateService.CollectStoredTraversePaths(restoredEntry);
        var remainingEntries = traverseGrantStateService.GetRemainingTraverseEntriesForCleanup(accountSid, existingLocations);
        var grantPaths = traverseGrantStateService.GetTraverseGrantPathsForCleanup(accountSid, []);
        var pathsToRemove = currentPaths
            .Where(pathToRemove =>
            {
                if (restoredPaths.Contains(pathToRemove, StringComparer.OrdinalIgnoreCase) ||
                    grantPaths.Contains(pathToRemove))
                {
                    return false;
                }

                return !remainingEntries.Any(entry => traverseGrantStateService.CollectStoredTraversePaths(entry)
                    .Contains(pathToRemove, StringComparer.OrdinalIgnoreCase));
            })
            .ToList();
        List<string> pathsToAdd;
        List<string> explicitTraversePathsToRestore;
        try
        {
            pathsToAdd = traverseCore.GetPathsNeedingTraverseAce(accountSid, restoredPaths)
                .Where(pathToAdd => !pathsToRemove.Contains(pathToAdd, StringComparer.OrdinalIgnoreCase))
                .ToList();
            explicitTraversePathsToRestore = CaptureExplicitTraverseAclPaths(accountSid, pathsToRemove);
        }
        catch (Exception ex)
        {
            throw new GrantOperationException(
                GrantApplyFailureStep.TraverseAclApply,
                normalizedPath,
                primaryConfigPath,
                ex);
        }

        List<string> appliedPaths = [];
        IReadOnlyList<GrantApplyWarning> warnings = [];
        bool durableSaveCompleted = storeModified;
        var mutationOrder = pathsToRemove.Count > 0
            ? pathsToAdd.Count > 0
                ? GrantMutationOrder.RemoveSaveAdd
                : GrantMutationOrder.ApplyThenSave
            : GrantMutationOrder.SaveThenApply;

        switch (mutationOrder)
        {
            case GrantMutationOrder.SaveThenApply:
                try
                {
                    traverseCore.TrackTraverse(accountSid, restoredEntry);

                    if (storeModified)
                    {
                        RestoreTraverseStoresToExactLocations(
                            accountSid,
                            existingLocations,
                            previousState.Locations,
                            mutate: true);
                        grantIntentStoreSaveService.Save(
                            affectedStores,
                            GrantApplyFailureStep.TraverseIntentSave,
                            normalizedPath);
                    }
                }
                catch (GrantOperationException ex)
                {
                    TryRestoreTrackedRuntimeTraverseEntry(accountSid, normalizedPath, runtimeSnapshot, ex);
                    if (storeModified)
                        TryRestoreTraverseSnapshots(storeOwnerSid, normalizedPath, snapshots, ex);
                    throw;
                }
                catch (Exception ex)
                {
                    var operationException = new GrantOperationException(
                        GrantApplyFailureStep.TraverseIntentSave,
                        normalizedPath,
                        primaryConfigPath,
                        ex);
                    TryRestoreTrackedRuntimeTraverseEntry(accountSid, normalizedPath, runtimeSnapshot, operationException);
                    if (storeModified)
                        TryRestoreTraverseSnapshots(storeOwnerSid, normalizedPath, snapshots, operationException);
                    throw operationException;
                }

                try
                {
                    if (pathsToAdd.Count > 0)
                        appliedPaths = traverseCore.ApplyTraverseAces(accountSid, pathsToAdd).ToList();

                    traverseCore.VerifyEffectiveTraverse(accountSid, restoredPaths);

                    if (AclHelper.IsContainerSid(accountSid))
                        containerIuSync.SyncTraverseToInteractiveUser(accountSid, normalizedPath);
                }
                catch (Exception ex)
                {
                    var operationException = new GrantOperationException(
                        GrantApplyFailureStep.TraverseAclApply,
                        normalizedPath,
                        primaryConfigPath,
                        ex is TraverseAclApplyException applyEx ? applyEx.InnerException ?? applyEx : ex);
                    var rollbackPaths = ex is TraverseAclApplyException applyFailure
                        ? applyFailure.AppliedPaths
                        : appliedPaths;
                    TryRollbackTraverseAcl(accountSid, rollbackPaths, primaryConfigPath, operationException);
                    TryRestoreTrackedRuntimeTraverseEntry(accountSid, normalizedPath, runtimeSnapshot, operationException);
                    if (storeModified)
                        TryRestoreTraverseSnapshots(storeOwnerSid, normalizedPath, snapshots, operationException);
                    throw operationException;
                }
                break;

            case GrantMutationOrder.ApplyThenSave:
                try
                {
                    if (pathsToRemove.Count > 0)
                        traverseCore.RemoveTraverseAces(accountSid, pathsToRemove);

                    if (storeModified)
                    {
                        RestoreTraverseStoresToExactLocations(
                            accountSid,
                            existingLocations,
                            previousState.Locations,
                            mutate: true);
                    }

                    traverseCore.TrackTraverse(accountSid, restoredEntry);
                }
                catch (Exception ex)
                {
                    var operationException = new GrantOperationException(
                        GrantApplyFailureStep.TraverseAclApply,
                        normalizedPath,
                        primaryConfigPath,
                        ex is TraverseAclApplyException applyEx ? applyEx.InnerException ?? applyEx : ex);
                    if (pathsToRemove.Count > 0)
                    {
                        TryReapplyRemovedTraverseAcl(
                            accountSid,
                            explicitTraversePathsToRestore,
                            normalizedPath,
                            primaryConfigPath,
                            operationException);
                    }

                    TryRestoreTrackedRuntimeTraverseEntry(accountSid, normalizedPath, runtimeSnapshot, operationException);
                    if (storeModified)
                        TryRestoreTraverseSnapshots(storeOwnerSid, normalizedPath, snapshots, operationException);
                    throw operationException;
                }

                if (storeModified)
                {
                    warnings = grantIntentStoreSaveService.SaveWithWarnings(
                        affectedStores,
                        GrantApplyFailureStep.PostTraverseRemoveSave,
                        normalizedPath);
                    durableSaveCompleted = warnings.Count == 0;
                }

                try
                {
                    traverseCore.VerifyEffectiveTraverse(accountSid, restoredPaths);

                    if (AclHelper.IsContainerSid(accountSid))
                        containerIuSync.SyncTraverseToInteractiveUser(accountSid, normalizedPath);
                }
                catch (Exception ex)
                {
                    var operationException = new GrantOperationException(
                        GrantApplyFailureStep.TraverseAclApply,
                        normalizedPath,
                        primaryConfigPath,
                        ex is TraverseAclApplyException applyEx ? applyEx.InnerException ?? applyEx : ex);
                    if (pathsToRemove.Count > 0)
                    {
                        TryReapplyRemovedTraverseAcl(
                            accountSid,
                            explicitTraversePathsToRestore,
                            normalizedPath,
                            primaryConfigPath,
                            operationException);
                    }

                    TryRestoreTrackedRuntimeTraverseEntry(accountSid, normalizedPath, runtimeSnapshot, operationException);
                    if (storeModified)
                        TryRestoreTraverseSnapshots(storeOwnerSid, normalizedPath, snapshots, operationException);
                    throw operationException;
                }
                break;

            case GrantMutationOrder.RemoveSaveAdd:
                try
                {
                    if (pathsToRemove.Count > 0)
                        traverseCore.RemoveTraverseAces(accountSid, pathsToRemove);

                    if (storeModified)
                    {
                        RestoreTraverseStoresToExactLocations(
                            accountSid,
                            existingLocations,
                            previousState.Locations,
                            mutate: true);
                        traverseCore.TrackTraverse(accountSid, restoredEntry);
                        grantIntentStoreSaveService.Save(
                            affectedStores,
                            GrantApplyFailureStep.TraverseIntentSave,
                            normalizedPath);
                    }
                    else
                    {
                        traverseCore.TrackTraverse(accountSid, restoredEntry);
                    }
                }
                catch (GrantOperationException ex)
                {
                    if (pathsToRemove.Count > 0)
                    {
                        TryReapplyRemovedTraverseAcl(
                            accountSid,
                            explicitTraversePathsToRestore,
                            normalizedPath,
                            primaryConfigPath,
                            ex);
                    }

                    TryRestoreTrackedRuntimeTraverseEntry(accountSid, normalizedPath, runtimeSnapshot, ex);
                    if (storeModified)
                        TryRestoreTraverseSnapshots(storeOwnerSid, normalizedPath, snapshots, ex);
                    throw;
                }
                catch (Exception ex)
                {
                    var operationException = new GrantOperationException(
                        GrantApplyFailureStep.TraverseAclApply,
                        normalizedPath,
                        primaryConfigPath,
                        ex is TraverseAclApplyException applyEx ? applyEx.InnerException ?? applyEx : ex);
                    if (pathsToRemove.Count > 0)
                    {
                        TryReapplyRemovedTraverseAcl(
                            accountSid,
                            explicitTraversePathsToRestore,
                            normalizedPath,
                            primaryConfigPath,
                            operationException);
                    }

                    TryRestoreTrackedRuntimeTraverseEntry(accountSid, normalizedPath, runtimeSnapshot, operationException);
                    if (storeModified)
                        TryRestoreTraverseSnapshots(storeOwnerSid, normalizedPath, snapshots, operationException);
                    throw operationException;
                }

                try
                {
                    if (pathsToAdd.Count > 0)
                        appliedPaths = traverseCore.ApplyTraverseAces(accountSid, pathsToAdd).ToList();

                    traverseCore.VerifyEffectiveTraverse(accountSid, restoredPaths);

                    if (AclHelper.IsContainerSid(accountSid))
                        containerIuSync.SyncTraverseToInteractiveUser(accountSid, normalizedPath);
                }
                catch (Exception ex)
                {
                    var operationException = new GrantOperationException(
                        GrantApplyFailureStep.TraverseAclApply,
                        normalizedPath,
                        primaryConfigPath,
                        ex is TraverseAclApplyException applyEx ? applyEx.InnerException ?? applyEx : ex);
                    var rollbackPaths = ex is TraverseAclApplyException applyFailure
                        ? applyFailure.AppliedPaths
                        : appliedPaths;
                    TryRollbackTraverseAcl(accountSid, rollbackPaths, primaryConfigPath, operationException);
                    if (pathsToRemove.Count > 0)
                    {
                        TryReapplyRemovedTraverseAcl(
                            accountSid,
                            explicitTraversePathsToRestore,
                            normalizedPath,
                            primaryConfigPath,
                            operationException);
                    }

                    TryRestoreTrackedRuntimeTraverseEntry(accountSid, normalizedPath, runtimeSnapshot, operationException);
                    if (storeModified)
                        TryRestoreTraverseSnapshots(storeOwnerSid, normalizedPath, snapshots, operationException);
                    throw operationException;
                }
                break;

            default:
                throw new InvalidOperationException($"Unexpected mutation order '{mutationOrder}'.");
        }

        return new GrantApplyResult(
            TraverseApplied: pathsToRemove.Count > 0 || appliedPaths.Count > 0,
            DatabaseModified: storeModified,
            DurableSaveCompleted: durableSaveCompleted,
            Warnings: warnings);
    }

    private GrantApplyResult RemoveTraverse(string accountSid, string normalizedPath)
    {
        var existingLocations = traverseIntentStoreCoordinator.GetTraverseLocationsForPath(
            accountSid,
            normalizedPath,
            includeManualSharedEntries: false);
        if (existingLocations.Count == 0)
            return default;

        var primaryConfigPath = grantIntentStoreSaveService.GetPrimaryConfigPath(
            existingLocations.Select(location => location.Store));
        var removingPaths = traverseGrantStateService.CollectStoredTraversePaths(existingLocations.Select(location => location.Entry));
        var remainingEntries = traverseGrantStateService.GetRemainingTraverseEntriesForCleanup(accountSid, existingLocations);
        var grantPaths = traverseGrantStateService.GetTraverseGrantPathsForCleanup(accountSid, []);

        try
        {
            traverseCore.RemoveTraverseAces(
                accountSid,
                removingPaths
                    .Where(pathToRemove =>
                    {
                        if (grantPaths.Contains(pathToRemove))
                            return false;

                        return !remainingEntries.Any(entry => traverseGrantStateService.CollectStoredTraversePaths(entry)
                            .Contains(pathToRemove, StringComparer.OrdinalIgnoreCase));
                    })
                    .ToList());
        }
        catch (Exception ex)
        {
            throw new GrantOperationException(
                GrantApplyFailureStep.TraverseAclRemove,
                normalizedPath,
                primaryConfigPath,
                ex);
        }

        RemoveTrackedTraverseWithoutFilesystem(accountSid, normalizedPath);
        RemoveTraverseEntriesFromStores(accountSid, normalizedPath, existingLocations);
        var warnings = grantIntentStoreSaveService.SaveWithWarnings(
            existingLocations.Select(location => location.Store),
            GrantApplyFailureStep.PostTraverseRemoveSave,
            normalizedPath);

        return new GrantApplyResult(
            TraverseApplied: true,
            DatabaseModified: true,
            DurableSaveCompleted: warnings.Count == 0,
            Warnings: warnings);
    }

    private bool RestoreTraverseStoresToExactLocations(
        string sid,
        IReadOnlyList<GrantIntentLocation> currentLocations,
        IReadOnlyList<GrantIntentRestoreLocation> desiredLocations,
        bool mutate)
    {
        var ownerSid = traverseIntentStoreCoordinator.ResolveStorageOwnerSid(sid);
        var desiredByConfigPath = desiredLocations
            .GroupBy(location => NormalizeConfigPath(location.ConfigPath), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        bool modified = false;

        foreach (var location in currentLocations)
        {
            var configPath = NormalizeConfigPath(location.Store.ConfigPath);
            if (!desiredByConfigPath.ContainsKey(configPath))
            {
                modified = true;
                if (mutate)
                    traverseIntentStoreCoordinator.RemoveTraverseEntryFromStore(sid, location.Store, location.Entry);
            }
        }

        foreach (var desired in desiredByConfigPath.Values)
        {
            var targetStore = GrantIntentStoreProvider.ResolveStore(desired.ConfigPath);
            var matchingLocations = currentLocations
                .Where(location =>
                    string.Equals(
                        NormalizeConfigPath(location.Store.ConfigPath),
                        NormalizeConfigPath(targetStore.ConfigPath),
                        StringComparison.OrdinalIgnoreCase))
                .ToList();
            GrantedPathEntry? currentEntry;
            if (matchingLocations.Count == 0)
            {
                currentEntry = null;
            }
            else if (!AclHelper.IsSpecificContainerSid(sid))
            {
                currentEntry = matchingLocations[0].Entry;
            }
            else if (desired.Entry.SourceSids?.Contains(sid, StringComparer.OrdinalIgnoreCase) == true)
            {
                currentEntry = matchingLocations
                    .FirstOrDefault(location => location.Entry.SourceSids?.Contains(sid, StringComparer.OrdinalIgnoreCase) == true)
                    ?.Entry;
            }
            else if (desired.Entry.SourceSids == null)
            {
                currentEntry = matchingLocations.FirstOrDefault(location => location.Entry.SourceSids == null)?.Entry;
            }
            else
            {
                currentEntry = matchingLocations[0].Entry;
            }
            if (currentEntry == null)
            {
                modified = true;
                if (mutate)
                    targetStore.AddEntry(ownerSid, desired.Entry);
                continue;
            }

            if (!traverseGrantStateService.EntriesEquivalent(currentEntry, desired.Entry))
            {
                modified = true;
                if (mutate)
                    targetStore.ReplaceEntry(ownerSid, currentEntry, desired.Entry);
            }
        }

        return modified;
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

    private void RemoveTraverseEntriesFromStores(
        string sid,
        string normalizedPath,
        IEnumerable<GrantIntentLocation> locations)
    {
        foreach (var location in locations)
        {
            if (!string.Equals(location.Entry.Path, normalizedPath, StringComparison.OrdinalIgnoreCase))
                continue;

            traverseIntentStoreCoordinator.RemoveTraverseEntryFromStore(sid, location.Store, location.Entry);
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
            dbAccessor.Write(db =>
            {
                var entries = snapshot != null
                    ? traverseIntentStoreCoordinator.GetOrCreateTraverseStore(db, sid)
                    : traverseIntentStoreCoordinator.GetTraverseStoreOrEmpty(db, sid);
                var currentEntry = FindRuntimeTraverseEntry(
                    db,
                    sid,
                    normalizedPath,
                    includeManualSharedEntries: true);
                if (currentEntry != null)
                    entries.Remove(currentEntry);

                if (snapshot != null)
                    entries.Add(snapshot.Clone());

                if (!AclHelper.IsSpecificContainerSid(sid))
                    db.RemoveAccountIfEmpty(sid);
            });
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
        GrantOperationException operationException)
    {
        if (appliedPaths.Count == 0)
            return;

        try
        {
            traverseCore.RemoveTraverseAces(sid, appliedPaths);
        }
        catch (Exception ex)
        {
            operationException.AppendCleanupFailure(
                GrantApplyFailureStep.TraverseAclRollback,
                appliedPaths[0],
                primaryConfigPath,
                ex);
        }
    }

    private List<string> CaptureExplicitTraverseAclPaths(
        string sid,
        IReadOnlyList<string> candidatePaths)
    {
        if (candidatePaths.Count == 0)
            return [];

        var traverseSid = traverseIntentStoreCoordinator.ResolveAclSid(sid);
        var identity = new SecurityIdentifier(traverseSid);
        var explicitPaths = new List<string>();

        foreach (var path in candidatePaths)
        {
            if (HasExplicitTraverseAcl(path, identity))
                explicitPaths.Add(path);
        }

        return explicitPaths;
    }

    private void TryReapplyRemovedTraverseAcl(
        string sid,
        IReadOnlyList<string> pathsToRestore,
        string normalizedPath,
        string? primaryConfigPath,
        GrantOperationException operationException)
    {
        if (pathsToRestore.Count == 0)
            return;

        try
        {
            var traverseSid = traverseIntentStoreCoordinator.ResolveAclSid(sid);
            var identity = new SecurityIdentifier(traverseSid);
            var missingPaths = pathsToRestore
                .Where(path => !HasExplicitTraverseAcl(path, identity))
                .ToList();
            if (missingPaths.Count == 0)
                return;

            traverseCore.ApplyTraverseAces(sid, missingPaths);
        }
        catch (Exception ex)
        {
            operationException.AppendCleanupFailure(
                GrantApplyFailureStep.TraverseAclRollback,
                normalizedPath,
                primaryConfigPath,
                ex);
        }
    }

    private bool HasExplicitTraverseAcl(string path, SecurityIdentifier identity)
    {
        if (!pathInfo.DirectoryExists(path))
            return false;

        return traverseAcl.HasExplicitTraverseAceOrThrow(path, identity);
    }

    private void RemoveTrackedTraverseWithoutFilesystem(string sid, string normalizedPath)
    {
        bool removed = traverseCore.RemoveTraverse(sid, normalizedPath, updateFileSystem: false);

        if (!removed && AclHelper.IsContainerSid(sid))
            containerIuSync.RevertInteractiveUserTraverse(sid, normalizedPath);
    }

    private GrantedPathEntry? FindRuntimeTraverseEntry(
        AppDatabase database,
        string sid,
        string normalizedPath,
        bool includeManualSharedEntries)
        => traverseGrantOwnerResolver.FindTraverseEntry(
            database,
            sid,
            normalizedPath,
            includeManualSharedEntries);

    private static string NormalizeConfigPath(string? configPath)
        => configPath == null ? string.Empty : Path.GetFullPath(configPath);
}
