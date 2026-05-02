using WinFormsTimer = System.Windows.Forms.Timer;

namespace RunFence.Infrastructure;

/// <summary>
/// Production <see cref="IUiTimerFactory"/> backed by <see cref="System.Windows.Forms.Timer"/>.
/// </summary>
public class WinFormsUiTimerFactory : IUiTimerFactory
{
    public IUiTimer Create() => new WinFormsUiTimer();

    private sealed class WinFormsUiTimer : IUiTimer
    {
        private readonly WinFormsTimer _timer = new();

        public int Interval
        {
            get => _timer.Interval;
            set => _timer.Interval = value;
        }

        public void Start() => _timer.Start();
        public void Stop() => _timer.Stop();
        public void Dispose() => _timer.Dispose();

        public event EventHandler Tick
        {
            add => _timer.Tick += value;
            remove => _timer.Tick -= value;
        }
    }
}
