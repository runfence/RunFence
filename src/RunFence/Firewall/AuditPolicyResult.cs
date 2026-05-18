namespace RunFence.Firewall;

public enum AuditPolicyStatus
{
    Succeeded,
    AccessDenied,
    Unsupported,
    ReadbackMismatch,
    Failed
}

public sealed record AuditPolicyResult(
    AuditPolicyStatus Status,
    bool RequestedState,
    bool? ObservedState,
    string? Error,
    bool IsRetryable);
