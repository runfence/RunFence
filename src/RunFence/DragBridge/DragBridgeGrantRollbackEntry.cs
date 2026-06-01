using RunFence.Acl;

namespace RunFence.DragBridge;

public sealed record DragBridgeGrantRollbackEntry(
    string Sid,
    string Path,
    GrantIntentRestoreSnapshot PreviousState);
