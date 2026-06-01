namespace RunFence.ForegroundMarker;

public sealed record class ForegroundPrivilegeMarkerMetadata(
    string ProcessName,
    string? AccountSid)
{
    public static ForegroundPrivilegeMarkerMetadata CreateFallback(uint processId) =>
        new($"PID {processId}", null);
}
