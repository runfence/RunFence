namespace RunFence.Acl.UI;

public class AclApplyPostProcessingPolicy(AclApplyPhaseCatalog phaseCatalog)
{
    public void CleanupCompletedPending(
        AclApplyPlan plan,
        AclApplyExecutionResult executionResult,
        AclManagerPendingChanges pending)
    {
        foreach (var descriptor in phaseCatalog.OrderedPhases)
        {
            switch (descriptor.Phase)
            {
                case AclApplyPhase.GrantAdd:
                    foreach (var entry in plan.PendingAdds)
                    {
                        if (executionResult.WasCompleted(descriptor.OperationKind, entry.Path, entry.IsDeny))
                            pending.Grants.RemoveGrant(entry.Path, entry.IsDeny);
                    }
                    break;
                case AclApplyPhase.GrantRemove:
                    foreach (var entry in plan.PendingRemoves)
                    {
                        if (executionResult.WasCompleted(descriptor.OperationKind, entry.Path, entry.IsDeny))
                            pending.Grants.CancelGrantRemoval(entry.Path, entry.IsDeny);
                    }
                    break;
                case AclApplyPhase.GrantModification:
                    foreach (var modification in plan.PendingModifications)
                    {
                        if (executionResult.WasCompleted(descriptor.OperationKind, modification.Entry.Path, modification.Entry.IsDeny))
                            pending.Grants.RemoveGrantModification(modification.Entry.Path, modification.Entry.IsDeny, out _);
                    }
                    break;
                case AclApplyPhase.GrantFix:
                    foreach (var entry in plan.PendingGrantFixes)
                    {
                        if (executionResult.WasCompleted(descriptor.OperationKind, entry.Path, entry.IsDeny))
                            pending.Grants.RemoveGrantFix(entry.Path, entry.IsDeny);
                    }
                    break;
                case AclApplyPhase.TraverseAdd:
                    foreach (var entry in plan.PendingTraverseAdds)
                    {
                        if (executionResult.WasCompleted(descriptor.OperationKind, entry.Path, null))
                            pending.Traverse.RemoveTraverse(entry.Path);
                    }
                    break;
                case AclApplyPhase.TraverseRemove:
                    foreach (var entry in plan.PendingTraverseRemoves)
                    {
                        if (executionResult.WasCompleted(descriptor.OperationKind, entry.Path, null))
                            pending.Traverse.CancelTraverseRemoval(entry.Path);
                    }
                    break;
                case AclApplyPhase.TraverseFix:
                    foreach (var entry in plan.PendingTraverseFixes)
                    {
                        if (executionResult.WasCompleted(descriptor.OperationKind, entry.Path, null))
                            pending.Traverse.RemoveTraverseFix(entry.Path);
                    }
                    break;
                case AclApplyPhase.GrantUntrack:
                    foreach (var entry in plan.PendingUntrackGrants)
                    {
                        if (executionResult.WasCompleted(descriptor.OperationKind, entry.Path, entry.IsDeny))
                            pending.Grants.RemoveUntrackedGrant(entry.Path, entry.IsDeny);
                    }
                    break;
                case AclApplyPhase.TraverseUntrack:
                    foreach (var entry in plan.PendingUntrackTraverse)
                    {
                        if (executionResult.WasCompleted(descriptor.OperationKind, entry.Path, null))
                            pending.Traverse.RemoveUntrackedTraverse(entry.Path);
                    }
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }

    public bool GrantConfigMoveHasCompletedOperation(
        PendingConfigMove move,
        AclApplyPlan plan,
        AclApplyExecutionResult executionResult)
    {
        var key = (move.Entry.Path, move.Entry.IsDeny);
        return executionResult.WasCompleted(phaseCatalog.GetOperationKind(AclApplyPhase.GrantAdd), key.Path, key.IsDeny) ||
               executionResult.WasCompleted(phaseCatalog.GetOperationKind(AclApplyPhase.GrantRemove), key.Path, key.IsDeny) ||
               executionResult.WasCompleted(phaseCatalog.GetOperationKind(AclApplyPhase.GrantUntrack), key.Path, key.IsDeny) ||
               plan.PendingModifications.Any(modification =>
                   string.Equals(modification.Entry.Path, key.Path, StringComparison.OrdinalIgnoreCase) &&
                   (modification.Entry.IsDeny == key.IsDeny || modification.NewIsDeny == key.IsDeny) &&
                   executionResult.WasCompleted(
                       phaseCatalog.GetOperationKind(AclApplyPhase.GrantModification),
                       modification.Entry.Path,
                       modification.Entry.IsDeny));
    }

    public bool TraverseConfigMoveHasCompletedOperation(
        PendingConfigMove move,
        AclApplyExecutionResult executionResult)
        => executionResult.WasCompleted(phaseCatalog.GetOperationKind(AclApplyPhase.TraverseAdd), move.Entry.Path, null) ||
           executionResult.WasCompleted(phaseCatalog.GetOperationKind(AclApplyPhase.TraverseRemove), move.Entry.Path, null) ||
           executionResult.WasCompleted(phaseCatalog.GetOperationKind(AclApplyPhase.TraverseUntrack), move.Entry.Path, null);

    public bool ShouldKeepGrantConfigMove(
        PendingConfigMove move,
        AclApplyPlan plan,
        AclApplyExecutionResult executionResult,
        IReadOnlySet<(string Path, bool IsDeny)> failedGrantMoves)
    {
        var key = (move.Entry.Path, move.Entry.IsDeny);
        return failedGrantMoves.Contains(key) ||
               executionResult.HasError(phaseCatalog.GetOperationKind(AclApplyPhase.GrantAdd), key.Path, key.IsDeny) ||
               executionResult.HasError(phaseCatalog.GetOperationKind(AclApplyPhase.GrantRemove), key.Path, key.IsDeny) ||
               executionResult.HasError(phaseCatalog.GetOperationKind(AclApplyPhase.GrantUntrack), key.Path, key.IsDeny) ||
               HasGrantModificationError(
                   plan.PendingModifications,
                   executionResult,
                   phaseCatalog.GetOperationKind(AclApplyPhase.GrantModification),
                   key.Path,
                   key.IsDeny) ||
               executionResult.HasError(phaseCatalog.GetOperationKind(AclApplyPhase.GrantFix), key.Path, key.IsDeny);
    }

    public bool ShouldKeepTraverseConfigMove(
        PendingConfigMove move,
        AclApplyExecutionResult executionResult,
        IReadOnlySet<string> failedTraverseMoves)
    {
        var key = move.Entry.Path;
        return failedTraverseMoves.Contains(key) ||
               executionResult.HasError(phaseCatalog.GetOperationKind(AclApplyPhase.TraverseAdd), key, null) ||
               executionResult.HasError(phaseCatalog.GetOperationKind(AclApplyPhase.TraverseRemove), key, null) ||
               executionResult.HasError(phaseCatalog.GetOperationKind(AclApplyPhase.TraverseUntrack), key, null) ||
               executionResult.HasError(phaseCatalog.GetOperationKind(AclApplyPhase.TraverseFix), key, null);
    }

    private static bool HasGrantModificationError(
        IEnumerable<PendingModification> modifications,
        AclApplyExecutionResult executionResult,
        AclPendingOperationKind operationKind,
        string path,
        bool isDeny)
        => modifications.Any(modification =>
            string.Equals(modification.Entry.Path, path, StringComparison.OrdinalIgnoreCase) &&
            (modification.Entry.IsDeny == isDeny || modification.NewIsDeny == isDeny) &&
            executionResult.HasError(operationKind, modification.Entry.Path, modification.Entry.IsDeny));
}
