namespace RunFence.Acl.UI;

public sealed record AclApplyPhaseDescriptor(
    AclApplyPhase Phase,
    AclPendingOperationKind OperationKind);
