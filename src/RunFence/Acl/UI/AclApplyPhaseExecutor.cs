using RunFence.Acl;
using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.Acl.UI;

public class AclApplyPhaseExecutor(
    ILoggingService log,
    IGrantMutatorService grantMutatorService,
    ITraverseService traverseService,
    AclApplySelectedStoreResolver selectedStoreResolver)
{
    private static readonly Func<bool> ConfirmGrantOperation = () => true;

    public async Task<int> ExecutePhaseAsync(
        AclApplyPhaseDescriptor descriptor,
        AclApplyPhaseExecutionContext context,
        int current)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(context);

        return descriptor.Phase switch
        {
            AclApplyPhase.GrantRemove => await RunPhaseAsync(
                context,
                context.Plan.PendingRemoves,
                entry => grantMutatorService.RemoveGrant(context.Sid, entry.Path, entry.IsDeny),
                entry => entry.Path,
                entry => entry.IsDeny,
                descriptor.OperationKind,
                current),
            AclApplyPhase.TraverseRemove => await RunPhaseAsync(
                context,
                context.Plan.PendingTraverseRemoves,
                entry => traverseService.RemoveTraverse(context.Sid, entry.Path),
                entry => entry.Path,
                _ => null,
                descriptor.OperationKind,
                current),
            AclApplyPhase.GrantUntrack => await RunPhaseAsync(
                context,
                context.Plan.PendingUntrackGrants,
                entry => grantMutatorService.UntrackGrant(context.Sid, entry.Path, entry.IsDeny),
                entry => entry.Path,
                entry => entry.IsDeny,
                descriptor.OperationKind,
                current),
            AclApplyPhase.TraverseUntrack => await RunPhaseAsync(
                context,
                context.Plan.PendingUntrackTraverse,
                entry => traverseService.UntrackTraverse(context.Sid, entry.Path),
                entry => entry.Path,
                _ => null,
                descriptor.OperationKind,
                current),
            AclApplyPhase.GrantAdd => await RunGrantAddPhaseAsync(context, descriptor.OperationKind, current),
            AclApplyPhase.GrantModification => await RunModificationPhaseAsync(context, descriptor.OperationKind, current),
            AclApplyPhase.TraverseAdd => await RunTraverseAddPhaseAsync(context, descriptor.OperationKind, current),
            AclApplyPhase.GrantFix => await RunPhaseAsync(
                context,
                context.Plan.PendingGrantFixes,
                entry => grantMutatorService.FixGrantAcl(context.Sid, entry.Path, entry.IsDeny),
                entry => entry.Path,
                entry => entry.IsDeny,
                descriptor.OperationKind,
                current),
            AclApplyPhase.TraverseFix => await RunPhaseAsync(
                context,
                context.Plan.PendingTraverseFixes,
                entry => traverseService.FixTraverseAcl(context.Sid, entry.Path),
                entry => entry.Path,
                _ => null,
                descriptor.OperationKind,
                current),
            _ => throw new ArgumentOutOfRangeException(nameof(descriptor), descriptor.Phase, null)
        };
    }

    private async Task<int> RunGrantAddPhaseAsync(
        AclApplyPhaseExecutionContext context,
        AclPendingOperationKind operationKind,
        int current)
    {
        foreach (var entry in context.Plan.PendingAdds)
        {
            try
            {
                var selectedStore = selectedStoreResolver.ResolveForGrantAdd(context.GrantConfigMoves, entry);
                var applyResult = await Task.Run(() =>
                    grantMutatorService.AddGrant(
                        context.Sid,
                        entry.Path,
                        entry.IsDeny,
                        entry.SavedRights,
                        GetConfirmation(entry.IsDeny),
                        selectedStore));
                AddWarnings(context.Result, applyResult.Warnings);
                context.Result.MarkCompleted(operationKind, entry.Path, entry.IsDeny);
            }
            catch (OperationCanceledException)
            {
                context.Result.WasCanceled = true;
                return current;
            }
            catch (GrantOperationException ex)
            {
                AddError(context.Result, operationKind, entry.Path, entry.IsDeny, ex);
            }
            catch (Exception ex)
            {
                SetFatalFailure(context.Result, operationKind, entry.Path, entry.IsDeny, ex);
                return current;
            }

            context.Progress.Report((++current, context.Total));
        }

        return current;
    }

    private async Task<int> RunModificationPhaseAsync(
        AclApplyPhaseExecutionContext context,
        AclPendingOperationKind operationKind,
        int current)
    {
        foreach (var modification in context.Plan.PendingModifications)
        {
            var entry = modification.Entry;
            try
            {
                var selectedStore = selectedStoreResolver.ResolveForGrantModification(context.GrantConfigMoves, modification);
                var rightsToApply = modification.NewRights ?? entry.SavedRights ?? SavedRightsState.DefaultForMode(modification.NewIsDeny);
                if (entry.IsDeny != modification.NewIsDeny)
                {
                    var applyResult = await Task.Run(() =>
                        grantMutatorService.SwitchGrantMode(
                            context.Sid,
                            entry.Path,
                            modification.NewIsDeny,
                            rightsToApply,
                            GetConfirmation(modification.NewIsDeny),
                            selectedStore));
                    AddWarnings(context.Result, applyResult.Warnings);
                    context.Result.MarkCompleted(operationKind, entry.Path, entry.IsDeny);
                }
                else
                {
                    var applyResult = await Task.Run(() =>
                        grantMutatorService.UpdateGrant(
                            context.Sid,
                            entry.Path,
                            modification.NewIsDeny,
                            rightsToApply,
                            GetConfirmation(modification.NewIsDeny),
                            selectedStore));
                    AddWarnings(context.Result, applyResult.Warnings);
                    context.Result.MarkCompleted(operationKind, entry.Path, entry.IsDeny);
                }
            }
            catch (OperationCanceledException)
            {
                context.Result.WasCanceled = true;
                return current;
            }
            catch (GrantOperationException ex)
            {
                AddError(context.Result, operationKind, entry.Path, entry.IsDeny, ex);
            }
            catch (Exception ex)
            {
                SetFatalFailure(context.Result, operationKind, entry.Path, entry.IsDeny, ex);
                return current;
            }

            context.Progress.Report((++current, context.Total));
        }

        return current;
    }

    private async Task<int> RunTraverseAddPhaseAsync(
        AclApplyPhaseExecutionContext context,
        AclPendingOperationKind operationKind,
        int current)
    {
        foreach (var entry in context.Plan.PendingTraverseAdds)
        {
            try
            {
                var selectedStore = selectedStoreResolver.ResolveForTraverseAdd(context.TraverseConfigMoves, entry);
                var applyResult = await Task.Run(() => traverseService.AddTraverse(context.Sid, entry.Path, selectedStore));
                AddWarnings(context.Result, applyResult.Warnings);
                context.Result.MarkCompleted(operationKind, entry.Path, null);
            }
            catch (OperationCanceledException)
            {
                context.Result.WasCanceled = true;
                return current;
            }
            catch (GrantOperationException ex)
            {
                AddError(context.Result, operationKind, entry.Path, null, ex);
            }
            catch (Exception ex)
            {
                SetFatalFailure(context.Result, operationKind, entry.Path, null, ex);
                return current;
            }

            context.Progress.Report((++current, context.Total));
        }

        return current;
    }

    private async Task<int> RunPhaseAsync<TItem>(
        AclApplyPhaseExecutionContext context,
        IEnumerable<TItem> items,
        Func<TItem, GrantApplyResult> operation,
        Func<TItem, string> pathSelector,
        Func<TItem, bool?> isDenySelector,
        AclPendingOperationKind operationKind,
        int current)
    {
        foreach (var item in items)
        {
            try
            {
                var applyResult = await Task.Run(() => operation(item));
                AddWarnings(context.Result, applyResult.Warnings);
                context.Result.MarkCompleted(operationKind, pathSelector(item), isDenySelector(item));
            }
            catch (OperationCanceledException)
            {
                context.Result.WasCanceled = true;
                return current;
            }
            catch (GrantOperationException ex)
            {
                AddError(context.Result, operationKind, pathSelector(item), isDenySelector(item), ex);
            }
            catch (Exception ex)
            {
                SetFatalFailure(context.Result, operationKind, pathSelector(item), isDenySelector(item), ex);
                return current;
            }

            context.Progress.Report((++current, context.Total));
        }

        return current;
    }

    private void AddError(
        AclApplyExecutionResult result,
        AclPendingOperationKind operationKind,
        string path,
        bool? isDeny,
        GrantOperationException ex)
    {
        log.Error(GrantApplyFailureFormatter.Format(ex.Step, ex.Path, ex.ConfigPath, ex.Cause), ex);
        result.Errors.Add(new AclApplyError(operationKind, path, isDeny, ex));
    }

    private void SetFatalFailure(
        AclApplyExecutionResult result,
        AclPendingOperationKind operationKind,
        string path,
        bool? isDeny,
        Exception cause)
    {
        var exception = new GrantOperationException(GetFatalFailureStep(operationKind), path, configPath: null, cause);
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
