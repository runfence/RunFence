namespace RunFence.Infrastructure;

public enum GroupMutationStatus
{
    Succeeded,
    NotFound,
    AlreadyExists,
    MemberMissing,
    AccessDenied,
    Failed
}
