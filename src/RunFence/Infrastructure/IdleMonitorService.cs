using RunFence.Core;

namespace RunFence.Infrastructure;

public class IdleMonitorService : IIdleMonitorService
{
    private readonly ILoggingService _log;
    private readonly ITimeProvider _timeProvider;
    private readonly ITimerScheduler _timerScheduler;

    private IDisposable? _scheduledTimer;
    private int _timeoutMinutes;
    private long _lastActivityTick;

    public event Action? IdleTimeoutReached;

    public IdleMonitorService(ILoggingService log,
        ITimeProvider timeProvider,
        ITimerScheduler timerScheduler)
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
        ScheduleNextCheck();

        _log.Info($"Idle monitor started: {_timeoutMinutes} minute timeout");
    }

    public void Stop()
    {
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

    private void ScheduleNextCheck()
    {
        _scheduledTimer = _timerScheduler.Schedule(OnTimerCallback, 10_000);
    }

    private void OnTimerCallback()
    {
        _scheduledTimer = null;

        if (_timeoutMinutes <= 0)
            return;

        var timeoutMs = (long)_timeoutMinutes * 60 * 1000;
        var msSinceActivity = _timeProvider.GetTickCount64() - _lastActivityTick;

        if (msSinceActivity >= timeoutMs)
        {
            _log.Info($"Idle timeout reached after {_timeoutMinutes} minutes");
            IdleTimeoutReached?.Invoke();
            ScheduleNextCheck();
        }
        else
        {
            ScheduleNextCheck();
        }
    }
}
