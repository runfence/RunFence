using Timer = System.Windows.Forms.Timer;

namespace RunFence.Startup.UI;

public class AutoLockTimerService : IAutoLockTimerService, IDisposable
{
    private Timer? _timer;

    public void Start(int timeoutSeconds, Action onTimeout)
    {
        Stop();
        var timeoutMs = (int)Math.Min((long)timeoutSeconds * 1000, int.MaxValue);
        _timer = new Timer { Interval = timeoutMs };
        _timer.Tick += (_, _) =>
        {
            Stop();
            onTimeout();
        };
        _timer.Start();
    }

    public void Stop()
    {
        if (_timer != null)
        {
            _timer.Stop();
            _timer.Dispose();
            _timer = null;
        }
    }

    public void Dispose() => Stop();
}