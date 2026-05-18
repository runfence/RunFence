using RunFence.Acl;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Persistence;

namespace RunFence.Acl.UI;

public class AclApplyExecutor(
    ILoggingService log,
    IPathGrantService pathGrantService,
    IGrantIntentStoreProvider grantIntentStoreProvider)
{
    private static readonly Func<bool> ConfirmGrantOperation = () => true;

    public async Task<AclApplyExecutionResult> ExecuteAsync(
        AclApplyPlan plan,
        string sid,
        bool isContainer,
        IProgress<(int current, int total)> progress)
    {
        var result = new AclApplyExecutionResult();
        int current = 0;
        var grantConfigMoves = plan.PendingConfigMoves.ToDictionary(
            move => (Path.GetFullPath(move.Entry.Path), move.Entry.IsDeny),
            move => move.TargetConfigPath,
            new GrantPathKeyComparer());
        var traverseConfigMoves = plan.PendingTraverseConfigMoves.ToDictionary(
            move => Path.GetFullPath(move.Entry.Path),
            move => move.TargetConfigPath,
            StringComparer.OrdinalIgnoreCase);

        current = await RunPhaseAsync(
            result,
            plan.PendingRemoves,
            e => pathGrantService.RemoveGrant(sid, e.Path, e.IsDeny),
            e => e.Path,
            e => e.IsDeny,
            AclPendingOperationKind.GrantRemove,
            progress,
            current,
            plan.TotalOperations);
        if (result.WasCanceled || result.HasFatalFailure)
            return result;

        current = await RunPhaseAsync(
            result,
            plan.PendingTraverseRemoves,
            e => pathGrantService.RemoveTraverse(sid, e.Path),
            e => e.Path,
            _ => null,
            AclPendingOperationKind.TraverseRemove,
            progress,
            current,
            plan.TotalOperations);
        if (result.WasCanceled || result.HasFatalFailure)
            return result;

        current = await RunPhaseAsync(
            result,
            plan.PendingUntrackGrants,
            e => pathGrantService.UntrackGrant(sid, e.Path, e.IsDeny),
            e => e.Path,
            e => e.IsDeny,
            AclPendingOperationKind.GrantUntrack,
            progress,
            current,
            plan.TotalOperations);
        if (result.WasCanceled || result.HasFatalFailure)
            return result;

        current = await RunPhaseAsync(
            result,
            plan.PendingUntrackTraverse,
            e => pathGrantService.UntrackTraverse(sid, e.Path),
            e => e.Path,
            _ => null,
            AclPendingOperationKind.TraverseUntrack,
            progress,
            current,
            plan.TotalOperations);
        if (result.WasCanceled || result.HasFatalFailure)
            return result;

        current = await RunGrantAddPhaseAsync(
            sid,
            plan.PendingAdds,
            grantConfigMoves,
            result,
            progress,
            current,
            plan.TotalOperations);
        if (result.WasCanceled || result.HasFatalFailure)
            return result;

        current = await RunModificationPhaseAsync(
            sid,
            plan.PendingModifications,
            grantConfigMoves,
            result,
            progress,
            current,
            plan.TotalOperations);
        if (result.WasCanceled || result.HasFatalFailure)
            return result;

        current = await RunTraverseAddPhaseAsync(
            sid,
            plan.PendingTraverseAdds,
            traverseConfigMoves,
            result,
            progress,
            current,
            plan.TotalOperations);
        if (result.WasCanceled || result.HasFatalFailure)
            return result;

        current = await RunPhaseAsync(
            result,
            plan.PendingGrantFixes,
            e => pathGrantService.FixGrantAcl(sid, e.Path, e.IsDeny),
            e => e.Path,
            e => e.IsDeny,
            AclPendingOperationKind.GrantFix,
            progress,
            current,
            plan.TotalOperations);
        if (result.WasCanceled || result.HasFatalFailure)
            return result;

        _ = await RunPhaseAsync(
            result,
            plan.PendingTraverseFixes,
            e => pathGrantService.FixTraverseAcl(sid, e.Path),
            e => e.Path,
            _ => null,
            AclPendingOperationKind.TraverseFix,
            progress,
            current,
            plan.TotalOperations);

        return result;
    }

    private async Task<int> RunGrantAddPhaseAsync(
        string sid,
        IEnumerable<GrantedPathEntry> entries,
        IReadOnlyDictionary<(string Path, bool IsDeny), string?> configMoves,
        AclApplyExecutionResult result,
        IProgress<(int current, int total)> progress,
        int current,
        int total)
    {
        foreach (var entry in entries)
        {
            try
            {
                var key = (entry.Path, entry.IsDeny);
                var selectedStore = ResolveSelectedStore(configMoves, key);
                var applyResult = await Task.Run(() =>
                    pathGrantService.AddGrant(
                        sid,
                        entry.Path,
                        entry.IsDeny,
                        entry.SavedRights,
                        GetConfirmation(entry.IsDeny),
                        selectedStore));
                AddWarnings(result, applyResult.Warnings);
                result.MarkCompleted(AclPendingOperationKind.GrantAdd, entry.Path, entry.IsDeny);
            }
            catch (OperationCanceledException)
            {
                result.WasCanceled = true;
                return current;
            }
            catch (GrantOperationException ex)
            {
                AddError(result, AclPendingOperationKind.GrantAdd, entry.Path, entry.IsDeny, ex);
            }
            catch (Exception ex)
            {
                SetFatalFailure(result, AclPendingOperationKind.GrantAdd, entry.Path, entry.IsDeny, ex);
                return current;
            }

            progress.Report((++current, total));
        }

        return current;
    }

    private async Task<int> RunModificationPhaseAsync(
        string sid,
        IEnumerable<PendingModification> modifications,
        IReadOnlyDictionary<(string Path, bool IsDeny), string?> configMoves,
        AclApplyExecutionResult result,
        IProgress<(int current, int total)> progress,
        int current,
        int total)
    {
        foreach (var mod in modifications)
        {
            var entry = mod.Entry;
            try
            {
                var selectedStore = ResolveSelectedStore(configMoves, entry, mod);
                var rightsToApply = mod.NewRights ?? entry.SavedRights ?? SavedRightsState.DefaultForMode(mod.NewIsDeny);
                if (entry.IsDeny != mod.NewIsDeny)
                {
                    var applyResult = await Task.Run(() =>
                        pathGrantService.SwitchGrantMode(
                            sid,
                            entry.Path,
                            mod.NewIsDeny,
                            rightsToApply,
                            GetConfirmation(mod.NewIsDeny),
                            selectedStore));
                    AddWarnings(result, applyResult.Warnings);
                    result.MarkCompleted(AclPendingOperationKind.GrantModification, entry.Path, entry.IsDeny);
                }
                else
                {
                    var applyResult = await Task.Run(() =>
                        pathGrantService.UpdateGrant(
                            sid,
                            entry.Path,
                            mod.NewIsDeny,
                            rightsToApply,
                            GetConfirmation(mod.NewIsDeny),
                            selectedStore));
                    AddWarnings(result, applyResult.Warnings);
                    result.MarkCompleted(AclPendingOperationKind.GrantModification, entry.Path, entry.IsDeny);
                }
            }
            catch (OperationCanceledException)
            {
                result.WasCanceled = true;
                return current;
            }
            catch (GrantOperationException ex)
            {
                AddError(result, AclPendingOperationKind.GrantModification, entry.Path, entry.IsDeny, ex);
            }
            catch (Exception ex)
            {
                SetFatalFailure(result, AclPendingOperationKind.GrantModification, mod.Entry.Path, mod.Entry.IsDeny, ex);
                return current;
            }

            progress.Report((++current, total));
        }

        return current;
    }

    private async Task<int> RunTraverseAddPhaseAsync(
        string sid,
        IEnumerable<GrantedPathEntry> entries,
        IReadOnlyDictionary<string, string?> configMoves,
        AclApplyExecutionResult result,
        IProgress<(int current, int total)> progress,
        int current,
        int total)
    {
        foreach (var entry in entries)
        {
            try
            {
                var selectedStore = ResolveSelectedStore(configMoves, entry.Path);
                var applyResult = await Task.Run(() => pathGrantService.AddTraverse(sid, entry.Path, selectedStore));
                AddWarnings(result, applyResult.Warnings);
                result.MarkCompleted(AclPendingOperationKind.TraverseAdd, entry.Path, null);
            }
            catch (OperationCanceledException)
            {
                result.WasCanceled = true;
                return current;
            }
            catch (GrantOperationException ex)
            {
                AddError(result, AclPendingOperationKind.TraverseAdd, entry.Path, null, ex);
            }
            catch (Exception ex)
            {
                SetFatalFailure(result, AclPendingOperationKind.TraverseAdd, entry.Path, null, ex);
                return current;
            }

            progress.Report((++current, total));
        }

        return current;
    }

    private async Task<int> RunPhaseAsync<TItem>(
        AclApplyExecutionResult result,
        IEnumerable<TItem> items,
        Func<TItem, GrantApplyResult> operation,
        Func<TItem, string> pathSelector,
        Func<TItem, bool?> isDenySelector,
        AclPendingOperationKind operationKind,
        IProgress<(int current, int total)> progress,
        int current,
        int total)
    {
        foreach (var item in items)
        {
            try
            {
                var applyResult = await Task.Run(() => operation(item));
                AddWarnings(result, applyResult.Warnings);
                result.MarkCompleted(operationKind, pathSelector(item), isDenySelector(item));
            }
            catch (OperationCanceledException)
            {
                result.WasCanceled = true;
                return current;
            }
            catch (GrantOperationException ex)
            {
                AddError(result, operationKind, pathSelector(item), isDenySelector(item), ex);
            }
            catch (Exception ex)
            {
                SetFatalFailure(result, operationKind, pathSelector(item), isDenySelector(item), ex);
                return current;
            }

            progress.Report((++current, total));
        }

        return current;
    }

    private void AddError(
        AclApplyExecutionResult? result,
        AclPendingOperationKind operationKind,
        string path,
        bool? isDeny,
        GrantOperationException ex)
    {
        log.Error(GrantApplyFailureFormatter.Format(ex.Step, ex.Path, ex.ConfigPath, ex.Cause), ex);
        if (result != null)
            result.Errors.Add(new AclApplyError(operationKind, path, isDeny, ex));
    }

    private void SetFatalFailure(
        AclApplyExecutionResult result,
        AclPendingOperationKind operationKind,
        string path,
        bool? isDeny,
        Exception cause)
    {
        var exception = new GrantOperationException(
            GetFatalFailureStep(operationKind),
            path,
            configPath: null,
            cause);
        log.Error(GrantApplyFailureFormatter.Format(exception.Step, exception.Path, exception.ConfigPath, exception.Cause), exception);
        result.SetFatalFailure(new AclApplyFatalFailure(operationKind, path, isDeny, exception));
    }

    private static void AddWarnings(
        AclApplyExecutionResult result,
        IReadOnlyList<GrantApplyWarning> warnings)
    {
        if (warnings.Count == 0)
            return;

        result.Warnings.AddRange(warnings);
    }

    private IGrantIntentStore? ResolveSelectedStore(
        IReadOnlyDictionary<(string Path, bool IsDeny), string?> configMoves,
        (string Path, bool IsDeny) key)
    {
        if (!configMoves.TryGetValue(key, out var targetConfigPath))
            return null;

        return grantIntentStoreProvider.ResolveStore(targetConfigPath);
    }

    private IGrantIntentStore? ResolveSelectedStore(
        IReadOnlyDictionary<(string Path, bool IsDeny), string?> configMoves,
        GrantedPathEntry entry,
        PendingModification mod)
    {
        var selectedStore = ResolveSelectedStore(configMoves, (entry.Path, mod.NewIsDeny));
        return selectedStore ?? ResolveSelectedStore(configMoves, (entry.Path, entry.IsDeny));
    }

    private IGrantIntentStore? ResolveSelectedStore(
        IReadOnlyDictionary<string, string?> configMoves,
        string path)
    {
        if (!configMoves.TryGetValue(path, out var targetConfigPath))
            return null;

        return grantIntentStoreProvider.ResolveStore(targetConfigPath);
    }

    private static Func<bool>? GetConfirmation(bool isDeny) => isDeny ? ConfirmGrantOperation : null;

    private static GrantApplyFailureStep GetFatalFailureStep(AclPendingOperationKind operationKind)
        => operationKind switch
        {
            AclPendingOperationKind.GrantAdd => GrantApplyFailureStep.GrantAclApply,
            AclPendingOperationKind.GrantRemove => GrantApplyFailureStep.GrantAclRemove,
            AclPendingOperationKind.GrantModification => GrantApplyFailureStep.GrantAclApply,
            AclPendingOperationKind.GrantFix => GrantApplyFailureStep.FixGrantAclApply,
            AclPendingOperationKind.TraverseAdd => GrantApplyFailureStep.TraverseAclApply,
            AclPendingOperationKind.TraverseRemove => GrantApplyFailureStep.TraverseAclRemove,
            AclPendingOperationKind.GrantUntrack => GrantApplyFailureStep.UntrackGrantSave,
            AclPendingOperationKind.TraverseUntrack => GrantApplyFailureStep.UntrackTraverseSave,
            AclPendingOperationKind.TraverseFix => GrantApplyFailureStep.FixTraverseAclApply,
            _ => throw new ArgumentOutOfRangeException(nameof(operationKind), operationKind, null)
        };
}
