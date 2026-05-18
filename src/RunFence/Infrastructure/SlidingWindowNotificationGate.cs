namespace RunFence.Infrastructure;

public sealed class SlidingWindowNotificationGate
{
    private readonly IClock _clock;
    private readonly TimeSpan _window;
    private readonly int _maxNotificationsPerWindow;
    private readonly Queue<DateTime> _notificationTimesUtc = new();
    private readonly object _sync = new();

    public SlidingWindowNotificationGate(IClock clock, TimeSpan window, int maxNotificationsPerWindow)
    {
        _clock = clock;
        _window = window;
        _maxNotificationsPerWindow = maxNotificationsPerWindow;
    }

    public bool TryAcquire()
    {
        lock (_sync)
        {
            var now = _clock.UtcNow;
            while (_notificationTimesUtc.Count > 0 && now - _notificationTimesUtc.Peek() >= _window)
                _notificationTimesUtc.Dequeue();

            if (_notificationTimesUtc.Count >= _maxNotificationsPerWindow)
                return false;

            _notificationTimesUtc.Enqueue(now);
            return true;
        }
    }
}
