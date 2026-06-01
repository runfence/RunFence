using RunFence.Startup;

namespace RunFence.ForegroundMarker;

public sealed class ForegroundPrivilegeMarkerService : IForegroundPrivilegeMarkerService, IDisposable
{
    private readonly IForegroundPrivilegeMarkerRuntime runtime;
    private readonly InteractiveUserRefreshCoordinator interactiveUserRefreshCoordinator;
    private bool disposed;

    public ForegroundPrivilegeMarkerService(
        IForegroundPrivilegeMarkerRuntime runtime,
        InteractiveUserRefreshCoordinator interactiveUserRefreshCoordinator)
    {
        this.runtime = runtime;
        this.interactiveUserRefreshCoordinator = interactiveUserRefreshCoordinator;
        interactiveUserRefreshCoordinator.InteractiveUserRefreshed += HandleInteractiveUserRefreshed;
    }

    public event Action<ForegroundPrivilegeMarkerState>? StateChanged
    {
        add
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            runtime.StateChanged += value;
        }
        remove
        {
            if (disposed)
                return;

            runtime.StateChanged -= value;
        }
    }

    public ForegroundPrivilegeMarkerState CurrentState
    {
        get
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return runtime.CurrentState;
        }
    }

    public void Start(bool markerWindowEnabled, bool markerWindowEnabledWhenFullscreen)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        runtime.Start(markerWindowEnabled, markerWindowEnabledWhenFullscreen);
    }

    public void SetMarkerWindowEnabled(bool enabled)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        runtime.SetMarkerWindowEnabled(enabled);
    }

    public void SetMarkerWindowEnabledWhenFullscreen(bool enabled)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        runtime.SetMarkerWindowEnabledWhenFullscreen(enabled);
    }

    public void Stop()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        runtime.Stop();
    }

    public void RefreshForegroundWindow()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        runtime.RefreshForegroundWindow();
    }

    public void OnInteractiveUserRefreshed()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        runtime.RequestReclassification();
    }

    public void Dispose()
    {
        if (disposed)
            return;

        interactiveUserRefreshCoordinator.InteractiveUserRefreshed -= HandleInteractiveUserRefreshed;
        runtime.Dispose();
        disposed = true;
    }

    private void HandleInteractiveUserRefreshed() => OnInteractiveUserRefreshed();
}
