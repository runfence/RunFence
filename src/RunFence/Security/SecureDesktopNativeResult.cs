namespace RunFence.Security;

public enum SecureDesktopNativeStatus
{
    Succeeded,
    Unavailable,
    AccessDenied,
    Failed
}

public sealed record SecureDesktopNativeResult(
    SecureDesktopNativeStatus Status,
    IntPtr OpenedDesktopHandle,
    string? OriginalDesktopIdentity,
    int? NativeErrorCode,
    IReadOnlyList<string>? CleanupWarnings);

