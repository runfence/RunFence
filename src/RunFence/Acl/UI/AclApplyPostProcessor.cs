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
    ITraverseGrantOwnerResolver traverseGrantOwnerResolver)
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
            .Where(path => executionResult.WasCompleted(AclPendingOperationKind.GrantAdd, path, isDeny: false))
            .ToList();
        var successfulRemoveAllowPaths = plan.PendingRemoves
            .Where(e => !e.IsDeny && !e.IsTraverseOnly)
            .Select(e => e.Path)
            .Where(path => executionResult.WasCompleted(AclPendingOperationKind.GrantRemove, path, isDeny: false))
            .ToList();

        RetainOnlyUncompletedEntries(
            plan.PendingAdds, pending.PendingAdds,
            e => executionResult.WasCompleted(AclPendingOperationKind.GrantAdd, e.Path, e.IsDeny),
            e => (e.Path, e.IsDeny));
        RetainOnlyUncompletedEntries(
            plan.PendingRemoves, pending.PendingRemoves,
            e => executionResult.WasCompleted(AclPendingOperationKind.GrantRemove, e.Path, e.IsDeny),
            e => (e.Path, e.IsDeny));
        RetainOnlyUncompletedEntries(
            plan.PendingModifications, pending.PendingModifications,
            m => executionResult.WasCompleted(AclPendingOperationKind.GrantModification, m.Entry.Path, m.Entry.IsDeny),
            m => (m.Entry.Path, m.Entry.IsDeny));
        RetainOnlyUncompletedEntries(
            plan.PendingGrantFixes, pending.PendingGrantFixes,
            e => executionResult.WasCompleted(AclPendingOperationKind.GrantFix, e.Path, e.IsDeny),
            e => (e.Path, e.IsDeny));
        RetainOnlyUncompletedEntries(
            plan.PendingTraverseAdds, pending.PendingTraverseAdds,
            e => executionResult.WasCompleted(AclPendingOperationKind.TraverseAdd, e.Path, null),
            e => e.Path);
        RetainOnlyUncompletedEntries(
            plan.PendingTraverseRemoves, pending.PendingTraverseRemoves,
            e => executionResult.WasCompleted(AclPendingOperationKind.TraverseRemove, e.Path, null),
            e => e.Path);
        RetainOnlyUncompletedEntries(
            plan.PendingTraverseFixes, pending.PendingTraverseFixes,
            e => executionResult.WasCompleted(AclPendingOperationKind.TraverseFix, e.Path, null),
            e => e.Path);
        RetainOnlyUncompletedEntries(
            plan.PendingUntrackGrants, pending.PendingUntrackGrants,
            e => executionResult.WasCompleted(AclPendingOperationKind.GrantUntrack, e.Path, e.IsDeny),
            e => (e.Path, e.IsDeny));
        RetainOnlyUncompletedEntries(
            plan.PendingUntrackTraverse, pending.PendingUntrackTraverse,
            e => executionResult.WasCompleted(AclPendingOperationKind.TraverseUntrack, e.Path, null),
            e => e.Path);
        foreach (var move in plan.PendingConfigMoves)
        {
            var key = (move.Entry.Path, move.Entry.IsDeny);
            var completedGrantOperation =
                executionResult.WasCompleted(AclPendingOperationKind.GrantAdd, key.Path, key.IsDeny) ||
                executionResult.WasCompleted(AclPendingOperationKind.GrantRemove, key.Path, key.IsDeny) ||
                executionResult.WasCompleted(AclPendingOperationKind.GrantUntrack, key.Path, key.IsDeny) ||
                plan.PendingModifications.Any(modification =>
                    string.Equals(modification.Entry.Path, key.Path, StringComparison.OrdinalIgnoreCase) &&
                    (modification.Entry.IsDeny == key.IsDeny || modification.NewIsDeny == key.IsDeny) &&
                    executionResult.WasCompleted(
                        AclPendingOperationKind.GrantModification,
                        modification.Entry.Path,
                        modification.Entry.IsDeny));
            if (shouldSkipFurtherMoves)
            {
                if (completedGrantOperation)
                    pending.PendingConfigMoves.Remove(key);

                continue;
            }

            var keep =
                configMoveResult.FailedGrantMoves.Contains(key) ||
                HasGrantError(executionResult.Errors, AclPendingOperationKind.GrantAdd, key.Path, key.IsDeny) ||
                HasGrantError(executionResult.Errors, AclPendingOperationKind.GrantRemove, key.Path, key.IsDeny) ||
                HasGrantError(executionResult.Errors, AclPendingOperationKind.GrantUntrack, key.Path, key.IsDeny) ||
                HasGrantModificationError(plan.PendingModifications, executionResult.Errors, key.Path, key.IsDeny) ||
                HasGrantError(executionResult.Errors, AclPendingOperationKind.GrantFix, key.Path, key.IsDeny);

            if (!keep)
                pending.PendingConfigMoves.Remove(key);
        }

        foreach (var move in plan.PendingTraverseConfigMoves)
        {
            var key = move.Entry.Path;
            var completedTraverseOperation =
                executionResult.WasCompleted(AclPendingOperationKind.TraverseAdd, key, null) ||
                executionResult.WasCompleted(AclPendingOperationKind.TraverseRemove, key, null) ||
                executionResult.WasCompleted(AclPendingOperationKind.TraverseUntrack, key, null);
            if (shouldSkipFurtherMoves)
            {
                if (completedTraverseOperation)
                    pending.PendingTraverseConfigMoves.Remove(key);

                continue;
            }

            var keep =
                configMoveResult.FailedTraverseMoves.Contains(key) ||
                HasTraverseError(executionResult.Errors, AclPendingOperationKind.TraverseAdd, key) ||
                HasTraverseError(executionResult.Errors, AclPendingOperationKind.TraverseRemove, key) ||
                HasTraverseError(executionResult.Errors, AclPendingOperationKind.TraverseUntrack, key) ||
                HasTraverseError(executionResult.Errors, AclPendingOperationKind.TraverseFix, key);
            if (!keep)
                pending.PendingTraverseConfigMoves.Remove(key);
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
                HasGrantError(executionErrors, AclPendingOperationKind.GrantFix, move.Entry.Path, move.Entry.IsDeny))
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
                HasTraverseError(executionErrors, AclPendingOperationKind.TraverseFix, move.Entry.Path))
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

    private static void RetainOnlyUncompletedEntries<TItem, TKey>(
        IEnumerable<TItem> items,
        Dictionary<TKey, TItem> pendingEntries,
        Func<TItem, bool> wasCompleted,
        Func<TItem, TKey> keySelector)
        where TKey : notnull
    {
        foreach (var item in items)
        {
            if (wasCompleted(item))
                pendingEntries.Remove(keySelector(item));
        }
    }

    private static bool HasGrantError(
        IReadOnlyList<AclApplyError> errors,
        AclPendingOperationKind operationKind,
        string path,
        bool isDeny)
        => errors.Any(error =>
            error.OperationKind == operationKind &&
            error.IsDeny == isDeny &&
            string.Equals(error.Path, path, StringComparison.OrdinalIgnoreCase));

    private static bool HasTraverseError(
        IReadOnlyList<AclApplyError> errors,
        AclPendingOperationKind operationKind,
        string path)
        => errors.Any(error =>
            error.OperationKind == operationKind &&
            string.Equals(error.Path, path, StringComparison.OrdinalIgnoreCase));

    private static bool HasGrantModificationError(
        IEnumerable<PendingModification> modifications,
        IReadOnlyList<AclApplyError> errors,
        string path,
        bool isDeny)
        => modifications.Any(modification =>
            string.Equals(modification.Entry.Path, path, StringComparison.OrdinalIgnoreCase) &&
            (modification.Entry.IsDeny == isDeny || modification.NewIsDeny == isDeny) &&
            HasGrantError(
                errors,
                AclPendingOperationKind.GrantModification,
                modification.Entry.Path,
                modification.Entry.IsDeny));

    private sealed record ConfigMoveApplyResult(
        IReadOnlyList<GrantOperationException> Errors,
        IReadOnlySet<(string Path, bool IsDeny)> FailedGrantMoves,
        IReadOnlySet<string> FailedTraverseMoves);
}
