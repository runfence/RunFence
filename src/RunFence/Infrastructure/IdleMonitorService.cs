using RunFence.Core;
using Timer = System.Windows.Forms.Timer;

namespace RunFence.Infrastructure;

public class IdleMonitorService : IIdleMonitorService
{
    private readonly ILoggingService _log;
    private readonly ITimeProvider _timeProvider;
    private readonly ITimerScheduler? _timerScheduler;

    private Timer? _timer;
    private IDisposable? _scheduledTimer;
    private int _timeoutMinutes;
    private long _lastActivityTick;

    public event Action? IdleTimeoutReached;

    public IdleMonitorService(ILoggingService log,
        ITimeProvider timeProvider,
        ITimerScheduler? timerScheduler = null)
    {
        _log = log;
        _timeProvider = timeProvider;
        _timerScheduler = timerScheduler;
        _lastActivityTick = _timeProvider.GetTickCount64();
    }

    public void Configure(int timeoutMinutes)
    {
        _timeoutMinutes = timeoutMinutes;
    }

    public void Start()
    {
        Stop();
        if (_timeoutMinutes <= 0)
            return;

        ResetIdleTimer();

        if (_timerScheduler != null)
        {
            // Test path: use injected scheduler (fires once; caller re-schedules if needed)
            _scheduledTimer = _timerScheduler.Schedule(OnTimerCallback, 10_000);
        }
        else
        {
            _timer = new Timer { Interval = 10_000 };
            _timer.Tick += (_, _) => OnTimerCallback();
            _timer.Start();
        }

        _log.Info($"Idle monitor started: {_timeoutMinutes} minute timeout");
    }

    public void Stop()
    {
        if (_timer != null)
        {
            _timer.Stop();
            _timer.Dispose();
            _timer = null;
        }

        _scheduledTimer?.Dispose();
        _scheduledTimer = null;
    }

    public void ResetIdleTimer()
    {
        _lastActivityTick = _timeProvider.GetTickCount64();
    }

    public void Dispose()
    {
        Stop();
    }

    private void OnTimerCallback()
    {
        if (_timeoutMinutes <= 0)
            return;

        var timeoutMs = (long)_timeoutMinutes * 60 * 1000;
        var msSinceActivity = _timeProvider.GetTickCount64() - _lastActivityTick;

        if (msSinceActivity >= timeoutMs)
        {
            _log.Info($"Idle timeout reached after {_timeoutMinutes} minutes");
            Stop();
            IdleTimeoutReached?.Invoke();
        }
    }
}