namespace RunFence.Ipc;

public record IpcCallerContext(
    string? CallerIdentity,
    string? CallerSid,
    bool IsAdmin,
    bool IdentityFromImpersonation)
{
    public string RateLimitKey => CallerSid ?? CallerIdentity ?? "unknown";
}
