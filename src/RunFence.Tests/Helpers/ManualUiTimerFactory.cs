using RunFence.Infrastructure;

namespace RunFence.Tests.Helpers;

public class ManualUiTimerFactory : IUiTimerFactory
{
    private readonly List<ManualUiTimer> _timers = [];

    public IReadOnlyList<ManualUiTimer> Timers => _timers;

    public IUiTimer Create()
    {
        var timer = new ManualUiTimer();
        _timers.Add(timer);
        return timer;
    }

    public sealed class ManualUiTimer : IUiTimer
    {
        public bool Enabled { get; private set; }
        public int Interval { get; set; }
        public int StartCallCount { get; private set; }
        public int StopCallCount { get; private set; }
        public int DisposeCallCount { get; private set; }
        public event EventHandler? Tick;

        public void Start()
        {
            StartCallCount++;
            Enabled = true;
        }

        public void Stop()
        {
            StopCallCount++;
            Enabled = false;
        }

        public void Fire()
        {
            if (!Enabled)
                throw new InvalidOperationException("Cannot fire a stopped timer.");

            Tick?.Invoke(this, EventArgs.Empty);
        }

        public void Dispose()
        {
            DisposeCallCount++;
            Enabled = false;
        }
    }
}
