using RunFence.Acl;

namespace RunFence.Acl.UI;

public sealed record AclApplyFatalFailure(
    AclPendingOperationKind OperationKind,
    string Path,
    bool? IsDeny,
    GrantOperationException Exception);
