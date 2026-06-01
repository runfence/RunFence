namespace RunFence.Launch;

public sealed class FolderHandlerTrackedSidState
{
    private readonly HashSet<string> _registeredSids = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _registeredSidsLock = new();

    public void Add(string sid)
    {
        lock (_registeredSidsLock)
            _registeredSids.Add(sid);
    }

    public void Remove(string sid)
    {
        lock (_registeredSidsLock)
            _registeredSids.Remove(sid);
    }

    public IReadOnlyList<string> Snapshot()
    {
        lock (_registeredSidsLock)
            return _registeredSids.ToList();
    }

    public void Merge(IEnumerable<string> activeSids, IEnumerable<string> staleTrackedSids)
    {
        lock (_registeredSidsLock)
        {
            foreach (var sid in staleTrackedSids)
                _registeredSids.Remove(sid);

            foreach (var sid in activeSids)
                _registeredSids.Add(sid);
        }
    }

    public void ExecuteLocked(Action action)
    {
        lock (_registeredSidsLock)
            action();
    }

    public T ExecuteLocked<T>(Func<T> action)
    {
        lock (_registeredSidsLock)
            return action();
    }
}
