using RunFence.Acl;

namespace RunFence.Acl.UI;

public sealed record AclApplyOutcome(
    bool Succeeded,
    IReadOnlyList<GrantOperationException> Errors,
    IReadOnlyList<GrantApplyWarning> Warnings);
