using System.Threading;

namespace RunFence.ForegroundMarker;

public sealed class ForegroundPrivilegeMarkerRuntime : IForegroundPrivilegeMarkerRuntime
{
    private readonly IForegroundMarkerThreadDispatcher dispatcher;
    private readonly IForegroundWinEventListener winEventListener;
    private readonly ForegroundPrivilegeRefreshCoordinator refreshCoordinator;
    private readonly IForegroundPrivilegeClassificationWorker classificationWorker;
    private ForegroundPrivilegeMarkerState currentState = ForegroundPrivilegeMarkerState.Inactive;
    private bool disposed;
    private bool stopped;
    private bool started;

    public ForegroundPrivilegeMarkerRuntime(
        IForegroundMarkerThreadDispatcher dispatcher,
        IForegroundWinEventListener winEventListener,
        ForegroundPrivilegeRefreshCoordinator refreshCoordinator,
        IForegroundPrivilegeClassificationWorker classificationWorker)
    {
        this.dispatcher = dispatcher;
        this.winEventListener = winEventListener;
        this.refreshCoordinator = refreshCoordinator;
        this.classificationWorker = classificationWorker;

        winEventListener.ForegroundChanged += HandleForegroundChanged;
        winEventListener.MoveSizeStarted += HandleMoveSizeStarted;
        winEventListener.MoveSizeEnded += HandleMoveSizeEnded;
        winEventListener.LocationChanged += HandleLocationChanged;
        refreshCoordinator.ClassificationRequested += HandleClassificationRequested;
        refreshCoordinator.StateChanged += HandleCoordinatorStateChanged;
    }

    public event Action<ForegroundPrivilegeMarkerState>? StateChanged;

    public ForegroundPrivilegeMarkerState CurrentState => Volatile.Read(ref currentState);

    public void Start(bool markerWindowEnabled, bool markerWindowEnabledWhenFullscreen)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (stopped)
            throw new InvalidOperationException("Foreground marker runtime cannot be restarted after Stop().");
        if (started)
        {
            dispatcher.Invoke(() =>
            {
                refreshCoordinator.SetMarkerWindowEnabled(markerWindowEnabled);
                refreshCoordinator.SetMarkerWindowEnabledWhenFullscreen(markerWindowEnabledWhenFullscreen);
            });
            return;
        }

        Volatile.Write(ref currentState, ForegroundPrivilegeMarkerState.Inactive);
        dispatcher.Start();
        started = true;

        dispatcher.Invoke(() =>
        {
            refreshCoordinator.SetRuntimeEnabled(true);
            refreshCoordinator.SetMarkerWindowEnabled(markerWindowEnabled);
            refreshCoordinator.SetMarkerWindowEnabledWhenFullscreen(markerWindowEnabledWhenFullscreen);
            winEventListener.Start();
            refreshCoordinator.RefreshForegroundWindow();
        });
    }

    public void Stop()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (stopped)
            return;

        if (!started)
        {
            stopped = true;
            return;
        }

        stopped = true;
        dispatcher.Invoke(StopMarkerThreadResources);

        dispatcher.Stop();
        started = false;
    }

    public void SetMarkerWindowEnabled(bool enabled)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (stopped)
            throw new InvalidOperationException("Foreground marker runtime is stopped.");
        if (!started)
            return;

        dispatcher.Invoke(() => refreshCoordinator.SetMarkerWindowEnabled(enabled));
    }

    public void SetMarkerWindowEnabledWhenFullscreen(bool enabled)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (stopped)
            throw new InvalidOperationException("Foreground marker runtime is stopped.");
        if (!started)
            return;

        dispatcher.Invoke(() => refreshCoordinator.SetMarkerWindowEnabledWhenFullscreen(enabled));
    }

    public void RefreshForegroundWindow()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (stopped)
            throw new InvalidOperationException("Foreground marker runtime is stopped.");
        if (!started)
            return;

        dispatcher.Post(refreshCoordinator.RefreshForegroundWindow);
    }

    public void RequestReclassification()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (stopped)
            throw new InvalidOperationException("Foreground marker runtime is stopped.");
        if (!started)
            return;

        dispatcher.Post(refreshCoordinator.RequestReclassification);
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        winEventListener.ForegroundChanged -= HandleForegroundChanged;
        winEventListener.MoveSizeStarted -= HandleMoveSizeStarted;
        winEventListener.MoveSizeEnded -= HandleMoveSizeEnded;
        winEventListener.LocationChanged -= HandleLocationChanged;

        if (started)
        {
            dispatcher.Invoke(DisposeMarkerThreadResources);
        }
        else
        {
            refreshCoordinator.StateChanged -= HandleCoordinatorStateChanged;
            refreshCoordinator.ClassificationRequested -= HandleClassificationRequested;
            refreshCoordinator.Dispose();
            winEventListener.Dispose();
        }

        dispatcher.Dispose();
    }

    private void HandleCoordinatorStateChanged(ForegroundPrivilegeMarkerState state)
    {
        Volatile.Write(ref currentState, state);
        StateChanged?.Invoke(state);
    }

    private void HandleForegroundChanged(IntPtr hwnd) => refreshCoordinator.RefreshForegroundWindow(hwnd);

    private void HandleMoveSizeStarted(IntPtr hwnd) => refreshCoordinator.OnMoveSizeStarted(hwnd);

    private void HandleMoveSizeEnded(IntPtr hwnd) => refreshCoordinator.OnMoveSizeEnded(hwnd);

    private void HandleLocationChanged(IntPtr hwnd) => refreshCoordinator.OnLocationChanged(hwnd);

    private void HandleClassificationRequested(ForegroundPrivilegeClassificationRequest request)
    {
        _ = RunClassificationAsync(request);
    }

    private void StopMarkerThreadResources()
    {
        winEventListener.Stop();
        refreshCoordinator.SetRuntimeEnabled(false);
    }

    private void DisposeMarkerThreadResources()
    {
        StopMarkerThreadResources();
        refreshCoordinator.StateChanged -= HandleCoordinatorStateChanged;
        refreshCoordinator.ClassificationRequested -= HandleClassificationRequested;
        refreshCoordinator.Dispose();
        winEventListener.Dispose();
    }

    private async Task RunClassificationAsync(ForegroundPrivilegeClassificationRequest request)
    {
        ForegroundPrivilegeClassificationResult result;
        try
        {
            result = await classificationWorker.ClassifyAsync(request, CancellationToken.None).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch
        {
            result = ForegroundPrivilegeClassificationResult.Hidden(request);
        }

        if (disposed || stopped || !started)
            return;

        try
        {
            dispatcher.Post(() =>
            {
                if (disposed || stopped)
                    return;

                try
                {
                    refreshCoordinator.ApplyClassificationResult(result);
                }
                catch (ObjectDisposedException)
                {
                }
            });
        }
        catch (ObjectDisposedException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }
}
