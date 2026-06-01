using RunFence.Infrastructure;

namespace RunFence.ForegroundMarker;

public sealed class JobKeeperStartupReconnectForegroundRefreshBridge(
    IJobKeeperStartupReconnectEvents reconnectEvents,
    IForegroundPrivilegeMarkerService foregroundPrivilegeMarkerService) : IRequiresInitialization, IDisposable
{
    private bool initialized;
    private bool disposed;

    public void Initialize()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (initialized)
            return;

        reconnectEvents.StartupReconnectCompleted += HandleStartupReconnectCompleted;
        initialized = true;
    }

    public void Dispose()
    {
        if (disposed)
            return;

        if (initialized)
            reconnectEvents.StartupReconnectCompleted -= HandleStartupReconnectCompleted;

        disposed = true;
    }

    private void HandleStartupReconnectCompleted(object? sender, JobKeeperStartupReconnectCompletedEventArgs e)
    {
        foregroundPrivilegeMarkerService.RefreshForegroundWindow();
    }
}
