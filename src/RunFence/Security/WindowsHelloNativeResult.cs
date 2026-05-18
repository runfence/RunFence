namespace RunFence.Security;

public enum WindowsHelloNativeStatus
{
    Verified,
    Canceled,
    Unavailable,
    Failed
}

public sealed record WindowsHelloNativeResult(
    WindowsHelloNativeStatus Status,
    int? NativeErrorCode,
    string? DisplayReason);

