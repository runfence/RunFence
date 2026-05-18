using RunFence.Persistence;

namespace RunFence.DragBridge;

public sealed record DragBridgeTraverseRollbackEntry(
    string Sid,
    string Path,
    GrantIntentRestoreSnapshot PreviousState);
