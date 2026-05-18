using RunFence.Acl;

namespace RunFence.Acl.UI;

public sealed record AclApplyError(
    AclPendingOperationKind OperationKind,
    string Path,
    bool? IsDeny,
    GrantOperationException Exception);
