namespace RunFence.Acl.UI;

public sealed class AclApplyPhaseCatalog
{
    private static readonly IReadOnlyList<AclApplyPhaseDescriptor> PhaseOrder =
    [
        new(AclApplyPhase.GrantRemove, AclPendingOperationKind.GrantRemove),
        new(AclApplyPhase.TraverseRemove, AclPendingOperationKind.TraverseRemove),
        new(AclApplyPhase.GrantUntrack, AclPendingOperationKind.GrantUntrack),
        new(AclApplyPhase.TraverseUntrack, AclPendingOperationKind.TraverseUntrack),
        new(AclApplyPhase.GrantAdd, AclPendingOperationKind.GrantAdd),
        new(AclApplyPhase.GrantModification, AclPendingOperationKind.GrantModification),
        new(AclApplyPhase.TraverseAdd, AclPendingOperationKind.TraverseAdd),
        new(AclApplyPhase.GrantFix, AclPendingOperationKind.GrantFix),
        new(AclApplyPhase.TraverseFix, AclPendingOperationKind.TraverseFix)
    ];

    public IReadOnlyList<AclApplyPhaseDescriptor> OrderedPhases => PhaseOrder;

    public int GetPhaseCount(AclApplyPhase phase, AclApplyPlan plan)
        => phase switch
        {
            AclApplyPhase.GrantRemove => plan.PendingRemoves.Count,
            AclApplyPhase.TraverseRemove => plan.PendingTraverseRemoves.Count,
            AclApplyPhase.GrantUntrack => plan.PendingUntrackGrants.Count,
            AclApplyPhase.TraverseUntrack => plan.PendingUntrackTraverse.Count,
            AclApplyPhase.GrantAdd => plan.PendingAdds.Count,
            AclApplyPhase.GrantModification => plan.PendingModifications.Count,
            AclApplyPhase.TraverseAdd => plan.PendingTraverseAdds.Count,
            AclApplyPhase.GrantFix => plan.PendingGrantFixes.Count,
            AclApplyPhase.TraverseFix => plan.PendingTraverseFixes.Count,
            _ => throw new ArgumentOutOfRangeException(nameof(phase), phase, null)
        };

    public AclPendingOperationKind GetOperationKind(AclApplyPhase phase)
        => OrderedPhases.FirstOrDefault(descriptor => descriptor.Phase == phase) is { } descriptor
            ? descriptor.OperationKind
            : throw new ArgumentOutOfRangeException(nameof(phase), phase, null);

    public int GetTotalOperations(AclApplyPlan plan)
        => OrderedPhases.Sum(descriptor => GetPhaseCount(descriptor.Phase, plan));

    public bool HasWork(AclApplyPlan plan)
        => GetTotalOperations(plan) > 0 ||
           plan.PendingConfigMoves.Count > 0 ||
           plan.PendingTraverseConfigMoves.Count > 0;
}
