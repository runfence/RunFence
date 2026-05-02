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

    public JobKeeperState? Take(string sid, bool isLow)
    {
        lock (_lock)
        {
            var keepers = GetKeepers(isLow);
            if (!keepers.Remove(sid, out var state))
                return null;

            return state;
        }
    }

    public IReadOnlyList<JobKeeperEntry> GetAll()
    {
        lock (_lock)
        {
            var result = new List<JobKeeperEntry>(_mediumKeepers.Count + _lowKeepers.Count);
            foreach (var (sid, state) in _mediumKeepers)
                result.Add(new JobKeeperEntry(sid, false, state.Pid));
            foreach (var (sid, state) in _lowKeepers)
                result.Add(new JobKeeperEntry(sid, true, state.Pid));
            return result;
        }
    }

    public IReadOnlyList<JobKeeperState> TakeAll()
    {
        lock (_lock)
        {
            var states = new List<JobKeeperState>(_mediumKeepers.Count + _lowKeepers.Count);
            states.AddRange(_mediumKeepers.Values);
            states.AddRange(_lowKeepers.Values);
            _mediumKeepers.Clear();
            _lowKeepers.Clear();
            return states;
        }
    }

    private Dictionary<string, JobKeeperState> GetKeepers(bool isLow) =>
        isLow ? _lowKeepers : _mediumKeepers;

    private void DisposePipe(JobKeeperState? state)
    {
        if (state == null)
            return;

        try { state.Pipe.Dispose(); } catch { }
    }
}
