using Microsoft.Win32.SafeHandles;

namespace RunFence.Infrastructure;

public sealed class BackupIntentNativeOpenResult(SafeFileHandle? handle, int status) : IDisposable
{
    public SafeFileHandle? Handle { get; } = handle;

    public int Status { get; } = status;

    public bool IsSuccess => Handle is { IsInvalid: false };

    public void Dispose()
    {
        Handle?.Dispose();
    }
}
