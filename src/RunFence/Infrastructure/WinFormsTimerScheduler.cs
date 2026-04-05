using Timer = System.Windows.Forms.Timer;

namespace RunFence.Infrastructure;

public class WinFormsTimerScheduler : ITimerScheduler
{
    public IDisposable Schedule(Action callback, int intervalMs)
    {
        var timer = new Timer { Interval = intervalMs };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            timer.Dispose();
            callback();
        };
        timer.Start();
        return new TimerDisposable(timer);
    }

    private sealed class TimerDisposable : IDisposable
    {
        private Timer? _timer;

        public TimerDisposable(Timer timer) => _timer = timer;

        public void Dispose()
        {
            var t = _timer;
            _timer = null;
            if (t == null)
                return;
            t.Stop();
            t.Dispose();
        }
    }
}