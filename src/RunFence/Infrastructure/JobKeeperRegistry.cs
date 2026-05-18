namespace RunFence.Infrastructure;

public sealed class JobKeeperRegistry : IJobKeeperRegistry
{
    private readonly Dictionary<string, JobKeeperState> _mediumKeepers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, JobKeeperState> _lowKeepers = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    public bool Has(string sid, bool isLow)
    {
        lock (_lock)
            return GetKeepers(isLow).ContainsKey(sid);
    }

    public void Register(string sid, bool isLow, JobKeeperState state)
    {
        JobKeeperState? replaced = null;
        lock (_lock)
        {
            var keepers = GetKeepers(isLow);
            if (keepers.TryGetValue(sid, out var current) && !ReferenceEquals(current, state))
                replaced = current;

            keepers[sid] = state;
        }

        DisposePipe(replaced);
    }

    public bool TryGet(string sid, bool isLow, out JobKeeperState state)
    {
        lock (_lock)
            return GetKeepers(isLow).TryGetValue(sid, out state!);
    }

    public void RemoveAndDispose(string sid, bool isLow, JobKeeperState? expectedState = null)
    {
        JobKeeperState? removed = null;
        lock (_lock)
        {
            var keepers = GetKeepers(isLow);
            if (keepers.TryGetValue(sid, out var current)
                && (expectedState == null || ReferenceEquals(current, expectedState)))
            {
                keepers.Remove(sid);
                removed = current;
            }
        }

        DisposePipe(removed);
    }

    private Dictionary<string, JobKeeperState> GetKeepers(bool isLow) =>
        isLow ? _lowKeepers : _mediumKeepers;

    private void DisposePipe(JobKeeperState? state)
    {
        if (state == null)
            return;

        try { state.Dispose(); } catch { }
    }
}
