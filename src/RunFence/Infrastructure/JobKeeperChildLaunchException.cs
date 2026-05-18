namespace RunFence.Infrastructure;

public sealed class JobKeeperChildLaunchException(string message, int nativeErrorCode)
    : InvalidOperationException(message)
{
    public int NativeErrorCode { get; } = nativeErrorCode;
}
