using RunFence.Persistence;

namespace RunFence.DragBridge;

public sealed record DragBridgeGrantRollbackEntry(
    string Sid,
    string Path,
    GrantIntentRestoreSnapshot PreviousState);
