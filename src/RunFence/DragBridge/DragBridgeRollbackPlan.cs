namespace RunFence.DragBridge;

public sealed record DragBridgeRollbackPlan(
    IReadOnlyList<DragBridgeGrantRollbackEntry> GrantRollbacks,
    IReadOnlyList<DragBridgeTraverseRollbackEntry> TraverseRollbacks,
    IReadOnlyList<string> TempPaths);
