using RunFence.Acl;
using RunFence.Acl.QuickAccess;
using RunFence.Acl.Traverse;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Persistence;

namespace RunFence.Acl.UI;

public class AclApplyPostProcessor(
    ILoggingService log,
    IGrantIntentRepository grantIntentRepository,
    IGrantIntentStoreProvider grantIntentStoreProvider,
    IQuickAccessPinService quickAccessPinService,
    ITraverseGrantOwnerResolver traverseGrantOwnerResolver,
    AclApplyPhaseCatalog phaseCatalog,
    AclApplyPostProcessingPolicy postProcessingPolicy)
{
    private static readonly GrantPathKeyComparer GrantPathComparer = new();

    public AclApplyOutcome Apply(
        AclApplyPlan plan,
        AclApplyExecutionResult executionResult,
        AclManagerPendingChanges pending,
        string sid,
        bool isContainer)
    {
        var errors = executionResult.Errors
            .Select(error => error.Exception)
            .ToList();
        var warnings = executionResult.Warnings.ToList();
        if (executionResult.HasFatalFailure)
            errors.Add(executionResult.FatalFailure!.Exception);

        var shouldSkipFurtherMoves = executionResult.WasCanceled || executionResult.HasFatalFailure;
        var configMoveResult = shouldSkipFurtherMoves
            ? new ConfigMoveApplyResult(
                [],
                new HashSet<(string Path, bool IsDeny)>(GrantPathComparer),
                new HashSet<string>(StringComparer.OrdinalIgnoreCase))
            : ApplyPureConfigOnlyMoves(plan, executionResult.Errors, sid, isContainer);
        if (!shouldSkipFurtherMoves)
            errors.AddRange(configMoveResult.Errors);

        var successfulAddAllowPaths = plan.PendingAdds
            .Where(e => !e.IsDeny && !e.IsTraverseOnly)
            .Select(e => e.Path)
            .Where(path => executionResult.WasCompleted(phaseCatalog.GetOperationKind(AclApplyPhase.GrantAdd), path, isDeny: false))
            .ToList();
        var successfulRemoveAllowPaths = plan.PendingRemoves
            .Where(e => !e.IsDeny && !e.IsTraverseOnly)
            .Select(e => e.Path)
            .Where(path => executionResult.WasCompleted(phaseCatalog.GetOperationKind(AclApplyPhase.GrantRemove), path, isDeny: false))
            .ToList();

        postProcessingPolicy.CleanupCompletedPending(plan, executionResult, pending);
        foreach (var move in plan.PendingConfigMoves)
        {
            var key = (move.Entry.Path, move.Entry.IsDeny);
            var completedGrantOperation = postProcessingPolicy.GrantConfigMoveHasCompletedOperation(move, plan, executionResult);
            if (shouldSkipFurtherMoves)
            {
                if (completedGrantOperation)
                    pending.Grants.RemoveGrantConfigMove(key.Path, key.IsDeny, out _);

                continue;
            }

            if (!postProcessingPolicy.ShouldKeepGrantConfigMove(move, plan, executionResult, configMoveResult.FailedGrantMoves))
                pending.Grants.RemoveGrantConfigMove(key.Path, key.IsDeny, out _);
        }

        foreach (var move in plan.PendingTraverseConfigMoves)
        {
            var key = move.Entry.Path;
            var completedTraverseOperation = postProcessingPolicy.TraverseConfigMoveHasCompletedOperation(move, executionResult);
            if (shouldSkipFurtherMoves)
            {
                if (completedTraverseOperation)
                    pending.Traverse.RemoveTraverseConfigMove(key, out _);

                continue;
            }

            if (!postProcessingPolicy.ShouldKeepTraverseConfigMove(move, executionResult, configMoveResult.FailedTraverseMoves))
                pending.Traverse.RemoveTraverseConfigMove(key, out _);
        }

        if (successfulAddAllowPaths.Count > 0)
            quickAccessPinService.PinFolders(sid, successfulAddAllowPaths);

        if (successfulRemoveAllowPaths.Count > 0)
            quickAccessPinService.UnpinFolders(sid, successfulRemoveAllowPaths);

        if (!executionResult.WasCanceled && errors.Count == 0)
            pending.Clear();

        return new AclApplyOutcome(!executionResult.WasCanceled && errors.Count == 0, errors, warnings);
    }

    private ConfigMoveApplyResult ApplyPureConfigOnlyMoves(
        AclApplyPlan plan,
        IReadOnlyList<AclApplyError> executionErrors,
        string sid,
        bool isContainer)
    {
        var errors = new List<GrantOperationException>();
        var failedGrantMoves = new HashSet<(string Path, bool IsDeny)>(GrantPathComparer);
        var failedTraverseMoves = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var move in plan.PendingConfigMoves)
        {
            if (plan.PendingAdds.Any(e =>
                    string.Equals(e.Path, move.Entry.Path, StringComparison.OrdinalIgnoreCase) &&
                    e.IsDeny == move.Entry.IsDeny) ||
                plan.PendingRemoves.Any(e =>
                    string.Equals(e.Path, move.Entry.Path, StringComparison.OrdinalIgnoreCase) &&
                    e.IsDeny == move.Entry.IsDeny) ||
                plan.PendingUntrackGrants.Any(e =>
                    string.Equals(e.Path, move.Entry.Path, StringComparison.OrdinalIgnoreCase) &&
                    e.IsDeny == move.Entry.IsDeny) ||
                plan.PendingModifications.Any(m =>
                    string.Equals(m.Entry.Path, move.Entry.Path, StringComparison.OrdinalIgnoreCase) &&
                    (m.Entry.IsDeny == move.Entry.IsDeny || m.NewIsDeny == move.Entry.IsDeny)))
            {
                continue;
            }

            if (plan.PendingGrantFixes.Any(e =>
                    string.Equals(e.Path, move.Entry.Path, StringComparison.OrdinalIgnoreCase) &&
                    e.IsDeny == move.Entry.IsDeny) &&
                executionErrors.Any(error =>
                    error.OperationKind == phaseCatalog.GetOperationKind(AclApplyPhase.GrantFix) &&
                    error.IsDeny == move.Entry.IsDeny &&
                    string.Equals(error.Path, move.Entry.Path, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            try
            {
                MoveEntryToSelectedStore(
                    [sid],
                    move.Entry.Clone(),
                    move.TargetConfigPath,
                    GrantApplyFailureStep.GrantIntentSave,
                    (ownerSid, entry) => grantIntentRepository.FindGrantLocations(ownerSid, entry),
                    includeLocation: null);
            }
            catch (GrantOperationException ex)
            {
                log.Error(GrantApplyFailureFormatter.Format(ex.Step, ex.Path, ex.ConfigPath, ex.Cause), ex);
                errors.Add(ex);
                failedGrantMoves.Add((move.Entry.Path, move.Entry.IsDeny));
            }
        }

        foreach (var move in plan.PendingTraverseConfigMoves)
        {
            if (plan.PendingTraverseAdds.Any(e => string.Equals(e.Path, move.Entry.Path, StringComparison.OrdinalIgnoreCase)) ||
                plan.PendingTraverseRemoves.Any(e => string.Equals(e.Path, move.Entry.Path, StringComparison.OrdinalIgnoreCase)) ||
                plan.PendingUntrackTraverse.Any(e => string.Equals(e.Path, move.Entry.Path, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (plan.PendingTraverseFixes.Any(e => string.Equals(e.Path, move.Entry.Path, StringComparison.OrdinalIgnoreCase)) &&
                executionErrors.Any(error =>
                    error.OperationKind == phaseCatalog.GetOperationKind(AclApplyPhase.TraverseFix) &&
                    string.Equals(error.Path, move.Entry.Path, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            try
            {
                var ownerSids = isContainer
                    ? new[] { sid, traverseGrantOwnerResolver.ResolveStorageOwnerSid(sid) }
                    : new[] { sid };
                MoveEntryToSelectedStore(
                    ownerSids,
                    move.Entry.Clone(),
                    move.TargetConfigPath,
                    GrantApplyFailureStep.TraverseIntentSave,
                    (ownerSid, entry) => grantIntentRepository.FindTraverseLocations(ownerSid, entry),
                    includeLocation: (ownerSid, location) => TraverseMoveLocationAppliesToSid(sid, ownerSid, location));
            }
            catch (GrantOperationException ex)
            {
                log.Error(GrantApplyFailureFormatter.Format(ex.Step, ex.Path, ex.ConfigPath, ex.Cause), ex);
                errors.Add(ex);
                failedTraverseMoves.Add(move.Entry.Path);
            }
        }

        return new ConfigMoveApplyResult(errors, failedGrantMoves, failedTraverseMoves);
    }

    private void MoveEntryToSelectedStore(
        IEnumerable<string> ownerSids,
        GrantedPathEntry entry,
        string? targetConfigPath,
        GrantApplyFailureStep saveFailureStep,
        Func<string, GrantedPathEntry, IReadOnlyList<GrantIntentLocation>> findLocations,
        Func<string, GrantIntentLocation, bool>? includeLocation)
    {
        var targetStore = grantIntentStoreProvider.ResolveStore(targetConfigPath);
        var affectedStores = new List<IGrantIntentStore>();
        var targetIsMainStore = targetStore.ConfigPath == null;

        foreach (var ownerSid in ownerSids.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var locations = findLocations(ownerSid, entry)
                .Where(location => includeLocation?.Invoke(ownerSid, location) ?? true)
                .ToList();
            if (locations.Count == 0)
            {
                locations = grantIntentRepository.FindEntriesForSid(ownerSid)
                    .Where(location =>
                        location.Entry.IsTraverseOnly == entry.IsTraverseOnly &&
                        location.Entry.IsDeny == entry.IsDeny &&
                        string.Equals(location.Entry.Path, entry.Path, StringComparison.OrdinalIgnoreCase) &&
                        (includeLocation?.Invoke(ownerSid, location) ?? true))
                    .ToList();
            }

            if (locations.Count == 0)
                continue;

            var mainLocation = locations.FirstOrDefault(location => location.Store.ConfigPath == null);
            var canonicalEntry = mainLocation?.Entry ?? locations[0].Entry;
            if (targetIsMainStore)
            {
                if (mainLocation == null)
                {
                    targetStore.AddEntry(ownerSid, canonicalEntry);
                    AddAffectedStore(affectedStores, grantIntentStoreProvider.MainStore);
                }

                foreach (var location in locations.Where(location => location.Store.ConfigPath != null))
                {
                    if (location.Store.RemoveEntry(ownerSid, location.Entry))
                        AddAffectedStore(affectedStores, location.Store);
                }
            }
            else
            {
                if (!locations.Any(location => ReferenceEquals(location.Store, targetStore)))
                {
                    targetStore.AddEntry(ownerSid, canonicalEntry);
                    AddAffectedStore(affectedStores, targetStore);
                }

                foreach (var location in locations.Where(location =>
                             location.Store.ConfigPath != null &&
                             !ReferenceEquals(location.Store, targetStore)))
                {
                    if (location.Store.RemoveEntry(ownerSid, location.Entry))
                        AddAffectedStore(affectedStores, location.Store);
                }

                if (mainLocation != null && grantIntentStoreProvider.MainStore.RemoveEntry(ownerSid, mainLocation.Entry))
                    AddAffectedStore(affectedStores, grantIntentStoreProvider.MainStore);
            }
        }

        foreach (var store in grantIntentStoreProvider.GetLoadedStores()
                     .Where(store => affectedStores.Any(affected => ReferenceEquals(affected, store))))
        {
            try
            {
                store.Save();
            }
            catch (Exception ex)
            {
                throw new GrantOperationException(saveFailureStep, entry.Path, store.ConfigPath, ex);
            }
        }
    }

    private bool TraverseMoveLocationAppliesToSid(
        string sid,
        string ownerSid,
        GrantIntentLocation location)
    {
        if (!traverseGrantOwnerResolver.UsesSharedContainerTraverse(sid) ||
            !string.Equals(ownerSid, traverseGrantOwnerResolver.ResolveStorageOwnerSid(sid), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return traverseGrantOwnerResolver.EntryAppliesToSid(
            location.Entry,
            sid,
            includeManualSharedEntries: true);
    }

    private static void AddAffectedStore(List<IGrantIntentStore> affectedStores, IGrantIntentStore store)
    {
        if (!affectedStores.Any(existing => ReferenceEquals(existing, store)))
            affectedStores.Add(store);
    }

    private sealed record ConfigMoveApplyResult(
        IReadOnlyList<GrantOperationException> Errors,
        IReadOnlySet<(string Path, bool IsDeny)> FailedGrantMoves,
        IReadOnlySet<string> FailedTraverseMoves);
}
