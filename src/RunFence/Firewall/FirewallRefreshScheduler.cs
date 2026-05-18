using RunFence.Core;
using RunFence.Infrastructure;

namespace RunFence.Firewall;

public class FirewallRefreshScheduler
{
    private readonly ITimerScheduler _timerScheduler;
    private readonly ILoggingService _log;
    private readonly Func<Task> _runRefreshCycleAsync;
    private readonly int _intervalMs;
    private readonly Lock _stateLock = new();

    private IDisposable? _timerRegistration;
    private Task? _workerTask;
    private int _isRefreshWorkerRunning;
    private int _refreshRequested;
    private bool _started;
    private bool _stopped;

    public FirewallRefreshScheduler(
        ITimerScheduler timerScheduler,
        ILoggingService log,
        Func<Task> runRefreshCycleAsync,
        int intervalMs = 60_000)
    {
        _timerScheduler = timerScheduler;
        _log = log;
        _runRefreshCycleAsync = runRefreshCycleAsync;
        _intervalMs = intervalMs;
    }

    public void Start()
    {
        lock (_stateLock)
        {
            if (_started || _stopped)
                return;

            _started = true;
        }

        ScheduleNextTimerTick();
    }

    public void Stop()
    {
        IDisposable? timerToDispose;
        lock (_stateLock)
        {
            _stopped = true;
            _started = false;
            timerToDispose = _timerRegistration;
            _timerRegistration = null;
        }

        timerToDispose?.Dispose();
    }

    public void RequestRefresh()
    {
        if (_stopped)
            return;

        Volatile.Write(ref _refreshRequested, 1);
        if (TryAcquireRefreshWorker())
            StartWorker();
    }

    public async Task DrainAsync(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_stopped)
                return;

            Task? workerTask;
            bool hasPendingRequest;
            bool workerRunning;
            lock (_stateLock)
            {
                workerTask = _workerTask;
                hasPendingRequest = Volatile.Read(ref _refreshRequested) == 1;
                workerRunning = Volatile.Read(ref _isRefreshWorkerRunning) == 1;
            }

            if (!hasPendingRequest && !workerRunning)
                return;

            if (!workerRunning)
            {
                if (TryAcquireRefreshWorker())
                    StartWorker();

                await Task.Yield();
                continue;
            }

            if (workerTask == null)
            {
                await Task.Yield();
                continue;
            }

            await workerTask.WaitAsync(cancellationToken);
        }
    }

    private void ScheduleNextTimerTick()
    {
        if (_stopped)
            return;

        IDisposable timer = _timerScheduler.Schedule(OnTimerTick, _intervalMs);
        lock (_stateLock)
        {
            if (_stopped || !_started)
            {
                timer.Dispose();
                return;
            }

            _timerRegistration = timer;
        }
    }

    private void OnTimerTick()
    {
        if (_stopped)
            return;

        RequestRefresh();
        ScheduleNextTimerTick();
    }

    private void StartWorker()
    {
        Task task = Task.Run(RunRefreshDrainLoopAsync);
        lock (_stateLock)
            _workerTask = task;
    }

    private async Task RunRefreshDrainLoopAsync()
    {
        while (true)
        {
            while (!_stopped && Interlocked.Exchange(ref _refreshRequested, 0) == 1)
            {
                try
                {
                    await _runRefreshCycleAsync();
                }
                catch (Exception ex)
                {
                    _log.Error("FirewallRefreshScheduler: DNS refresh cycle failed", ex);
                }
            }

            if (ReleaseRefreshWorkerAndShouldContinue())
                continue;

            return;
        }
    }

    private bool TryAcquireRefreshWorker() =>
        Interlocked.CompareExchange(ref _isRefreshWorkerRunning, 1, 0) == 0;

    private bool ReleaseRefreshWorkerAndShouldContinue()
    {
        Volatile.Write(ref _isRefreshWorkerRunning, 0);
        if (_stopped || Volatile.Read(ref _refreshRequested) == 0)
            return false;

        return TryAcquireRefreshWorker();
    }
}
