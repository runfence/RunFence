using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Persistence;

namespace RunFence.Acl.Traverse;

public class TraverseRestoreWorkflow(
    ITraverseCoreOperations traverseCore,
    ContainerInteractiveUserSync containerIuSync,
    ITraverseIntentStoreCoordinator traverseIntentStoreCoordinator,
    TraverseGrantStateService traverseGrantStateService,
    GrantMutationOrderResolver grantMutationOrderResolver,
    TraverseRestoreStateRestorer traverseRestoreStateRestorer,
    TraverseRestoreAclRollbackService traverseRestoreAclRollbackService,
    Func<IGrantIntentStoreProvider> grantIntentStoreProvider,
    IGrantIntentStoreSaveService grantIntentStoreSaveService)
{
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
            .Select(location => GrantIntentStoreProvider.ResolveStore(location.StoreIdentity.ConfigPath))
            .Distinct()
            .ToList();
        var affectedStores = existingLocations.Select(location => location.Store)
            .Concat(finalStores)
            .Distinct()
            .ToList();
        var storeOwnerSid = traverseIntentStoreCoordinator.ResolveStorageOwnerSid(accountSid);
        var snapshots = traverseRestoreStateRestorer.CaptureStoreSnapshots(storeOwnerSid, normalizedPath, affectedStores);
        var runtimeSnapshot = traverseRestoreStateRestorer.CaptureRuntimeSnapshot(accountSid, normalizedPath);
        var primaryConfigPath = grantIntentStoreSaveService.GetPrimaryConfigPath(affectedStores);
        bool storeModified = RestoreTraverseStoresToExactLocations(
            accountSid,
            existingLocations,
            previousState.Locations,
            mutate: false);

        var currentTrackedEntries = existingLocations.Select(location => location.Entry).ToList();
        if (currentTrackedEntries.Count == 0 && runtimeSnapshot.Entry != null)
            currentTrackedEntries.Add(runtimeSnapshot.Entry);

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
            explicitTraversePathsToRestore = traverseRestoreAclRollbackService.CaptureExplicitTraverseAclPaths(
                accountSid,
                pathsToRemove);
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
        var mutationOrder = grantMutationOrderResolver.ForAclDelta(
            hasAclAdditions: pathsToAdd.Count > 0,
            hasAclRemovals: pathsToRemove.Count > 0);

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
                    traverseRestoreStateRestorer.TryRestoreRuntimeTraverseEntry(runtimeSnapshot, ex);
                    if (storeModified)
                        traverseRestoreStateRestorer.TryRestoreStoreSnapshots(snapshots, ex);
                    throw;
                }
                catch (Exception ex)
                {
                    var operationException = new GrantOperationException(
                        GrantApplyFailureStep.TraverseIntentSave,
                        normalizedPath,
                        primaryConfigPath,
                        ex);
                    traverseRestoreStateRestorer.TryRestoreRuntimeTraverseEntry(runtimeSnapshot, operationException);
                    if (storeModified)
                        traverseRestoreStateRestorer.TryRestoreStoreSnapshots(snapshots, operationException);
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
                    traverseRestoreAclRollbackService.TryRollbackTraverseAcl(
                        accountSid,
                        rollbackPaths,
                        primaryConfigPath,
                        operationException);
                    traverseRestoreStateRestorer.TryRestoreRuntimeTraverseEntry(runtimeSnapshot, operationException);
                    if (storeModified)
                        traverseRestoreStateRestorer.TryRestoreStoreSnapshots(snapshots, operationException);
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
                        traverseRestoreAclRollbackService.TryReapplyRemovedTraverseAcl(
                            accountSid,
                            explicitTraversePathsToRestore,
                            normalizedPath,
                            primaryConfigPath,
                            operationException);
                    }

                    traverseRestoreStateRestorer.TryRestoreRuntimeTraverseEntry(runtimeSnapshot, operationException);
                    if (storeModified)
                        traverseRestoreStateRestorer.TryRestoreStoreSnapshots(snapshots, operationException);
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
                        traverseRestoreAclRollbackService.TryReapplyRemovedTraverseAcl(
                            accountSid,
                            explicitTraversePathsToRestore,
                            normalizedPath,
                            primaryConfigPath,
                            operationException);
                    }

                    traverseRestoreStateRestorer.TryRestoreRuntimeTraverseEntry(runtimeSnapshot, operationException);
                    if (storeModified)
                        traverseRestoreStateRestorer.TryRestoreStoreSnapshots(snapshots, operationException);
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
                        traverseRestoreAclRollbackService.TryReapplyRemovedTraverseAcl(
                            accountSid,
                            explicitTraversePathsToRestore,
                            normalizedPath,
                            primaryConfigPath,
                            ex);
                    }

                    traverseRestoreStateRestorer.TryRestoreRuntimeTraverseEntry(runtimeSnapshot, ex);
                    if (storeModified)
                        traverseRestoreStateRestorer.TryRestoreStoreSnapshots(snapshots, ex);
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
                        traverseRestoreAclRollbackService.TryReapplyRemovedTraverseAcl(
                            accountSid,
                            explicitTraversePathsToRestore,
                            normalizedPath,
                            primaryConfigPath,
                            operationException);
                    }

                    traverseRestoreStateRestorer.TryRestoreRuntimeTraverseEntry(runtimeSnapshot, operationException);
                    if (storeModified)
                        traverseRestoreStateRestorer.TryRestoreStoreSnapshots(snapshots, operationException);
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
                    traverseRestoreAclRollbackService.TryRollbackTraverseAcl(
                        accountSid,
                        rollbackPaths,
                        primaryConfigPath,
                        operationException);
                    if (pathsToRemove.Count > 0)
                    {
                        traverseRestoreAclRollbackService.TryReapplyRemovedTraverseAcl(
                            accountSid,
                            explicitTraversePathsToRestore,
                            normalizedPath,
                            primaryConfigPath,
                            operationException);
                    }

                    traverseRestoreStateRestorer.TryRestoreRuntimeTraverseEntry(runtimeSnapshot, operationException);
                    if (storeModified)
                        traverseRestoreStateRestorer.TryRestoreStoreSnapshots(snapshots, operationException);
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
            .GroupBy(location => NormalizeConfigPath(location.StoreIdentity.ConfigPath), StringComparer.OrdinalIgnoreCase)
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
            var targetStore = GrantIntentStoreProvider.ResolveStore(desired.StoreIdentity.ConfigPath);
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

    private void RemoveTrackedTraverseWithoutFilesystem(string sid, string normalizedPath)
    {
        bool removed = traverseCore.RemoveTraverse(sid, normalizedPath, updateFileSystem: false);

        if (!removed && AclHelper.IsContainerSid(sid))
            containerIuSync.RevertInteractiveUserTraverse(sid, normalizedPath);
    }

    private static string NormalizeConfigPath(string? configPath)
        => configPath == null ? string.Empty : Path.GetFullPath(configPath);
}
