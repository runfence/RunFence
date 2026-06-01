namespace RunFence.Infrastructure;

public sealed class OwnedJobHandle : IDisposable
{
    private readonly IJobObjectApi jobObjectApi;
    private IntPtr handle;
    private bool disposed;

    public OwnedJobHandle(IJobObjectApi jobObjectApi, IntPtr handle)
    {
        this.jobObjectApi = jobObjectApi ?? throw new ArgumentNullException(nameof(jobObjectApi));
        if (handle == IntPtr.Zero)
            throw new ArgumentException("Owned job handle must be nonzero.", nameof(handle));

        this.handle = handle;
    }

    public IntPtr Handle
    {
        get
        {
            ThrowIfDisposed();
            return handle;
        }
    }

    public IntPtr Release()
    {
        ThrowIfDisposed();
        var releasedHandle = handle;
        handle = IntPtr.Zero;
        disposed = true;
        return releasedHandle;
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        if (handle != IntPtr.Zero)
        {
            jobObjectApi.CloseHandle(handle);
            handle = IntPtr.Zero;
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
    }
}
