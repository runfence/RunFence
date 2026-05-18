namespace RunFence.Launch;

public class FolderHandlerSidLockProvider
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, object> _sidLocks = new(StringComparer.OrdinalIgnoreCase);

    public IFolderHandlerSidLock Acquire(string sid)
    {
        object sidLock;
        lock (_gate)
        {
            if (!_sidLocks.TryGetValue(sid, out sidLock!))
            {
                sidLock = new object();
                _sidLocks[sid] = sidLock;
            }
        }

        Monitor.Enter(sidLock);
        return new FolderHandlerSidLock(sidLock);
    }

    private sealed class FolderHandlerSidLock(object sidLock) : IFolderHandlerSidLock
    {
        private object? _sidLock = sidLock;

        public void Dispose()
        {
            var current = Interlocked.Exchange(ref _sidLock, null);
            if (current != null)
                Monitor.Exit(current);
        }
    }
}
