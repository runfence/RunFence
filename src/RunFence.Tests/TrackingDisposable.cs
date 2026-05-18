namespace RunFence.Tests;

/// <summary>
/// Test helper that tracks whether <see cref="Dispose"/> has been called and
/// provides deterministic event-based waiting for asynchronous disposal.
/// </summary>
public sealed class TrackingDisposable : IDisposable
{
    private readonly ManualResetEventSlim _disposedEvent = new();
    private int _eventDisposed; // 0 = live, 1 = disposed; Interlocked guard against double-dispose

    public bool IsDisposed { get; private set; }

    public void Dispose()
    {
        IsDisposed = true;
        _disposedEvent.Set();
        if (Interlocked.CompareExchange(ref _eventDisposed, 1, 0) == 0)
            _disposedEvent.Dispose();
    }

    /// <summary>
    /// Waits up to five seconds for <see cref="Dispose"/> to be called.
    /// Returns <see langword="true"/> if disposal occurred within the timeout.
    /// </summary>
    public bool WaitUntilDisposed()
    {
        bool result;
        try
        {
            result = _disposedEvent.Wait(TimeSpan.FromSeconds(5));
        }
        catch (ObjectDisposedException)
        {
            // Dispose() was called and cleaned up the event before Wait completed.
            // Since Set() always precedes Dispose() in our Dispose() method, IsDisposed is true.
            return IsDisposed;
        }
        if (Interlocked.CompareExchange(ref _eventDisposed, 1, 0) == 0)
            _disposedEvent.Dispose();
        return result;
    }
}
