using System.Security.AccessControl;

namespace RunFence.Acl.Permissions;

public enum AclAccessEvaluationStatus
{
    Allowed,
    Denied,
    Partial,
    Failed
}

public enum AclAccessEvaluationSource
{
    DeterministicSidSet
}

public readonly record struct AclAccessEvaluationResult(
    FileSystemRights RequestedRights,
    FileSystemRights GrantedRights,
    FileSystemRights DeniedRights,
    string? BlockingSid,
    AclAccessEvaluationSource Source,
    AclAccessEvaluationStatus Status,
    string? Error);

