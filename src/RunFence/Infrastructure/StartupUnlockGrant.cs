namespace RunFence.Infrastructure;

public class StartupUnlockGrant(IStopwatchProvider stopwatch) : IStartupUnlockGrant
{
    private const double GrantLifetimeSeconds = 60;
    private readonly object _lock = new();
    private bool _available;
    private long _grantedAt;

    public void Grant()
    {
        lock (_lock)
        {
            _available = true;
            _grantedAt = stopwatch.GetTimestamp();
        }
    }

    public bool TryConsume()
    {
        lock (_lock)
        {
            if (!_available)
                return false;

            var now = stopwatch.GetTimestamp();
            _available = false;
            return stopwatch.GetElapsedSeconds(_grantedAt, now) <= GrantLifetimeSeconds;
        }
    }
}
