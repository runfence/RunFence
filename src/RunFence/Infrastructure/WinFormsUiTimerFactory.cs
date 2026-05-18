using Timer = System.Windows.Forms.Timer;

namespace RunFence.Infrastructure;

public class WinFormsUiTimerFactory : IUiTimerFactory
{
    public IUiTimer Create() => new WinFormsUiTimer();

    private sealed class WinFormsUiTimer : IUiTimer
    {
        private readonly Timer _timer = new();

        public bool Enabled => _timer.Enabled;
        public int Interval
        {
            get => _timer.Interval;
            set => _timer.Interval = value;
        }

        public event EventHandler? Tick
        {
            add => _timer.Tick += value;
            remove => _timer.Tick -= value;
        }

        public void Start() => _timer.Start();
        public void Stop() => _timer.Stop();

        public void Dispose()
        {
            _timer.Stop();
            _timer.Dispose();
        }
    }
}
