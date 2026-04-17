using RunFence.Core;
using RunFence.Core.Ipc;
using RunFence.Infrastructure;

namespace RunFence.Ipc;

/// <summary>
/// Wraps <see cref="IUiThreadInvoker.Invoke"/> with shutdown-safe error handling for IPC handlers.
/// Translates <see cref="ObjectDisposedException"/> and <see cref="InvalidOperationException"/>
/// (thrown when the UI form is being disposed during shutdown) into a structured IPC shutdown response.
/// </summary>
public class IpcUiInvoker(IUiThreadInvoker uiThreadInvoker, IAppStateProvider appState)
{
    private static readonly IpcResponse ShuttingDownResponse =
        new() { Success = false, ErrorMessage = "Application is shutting down." };

    /// <summary>
    /// Invokes <paramref name="action"/> on the UI thread. If the application is shutting down
    /// (indicated by <see cref="ObjectDisposedException"/> or <see cref="InvalidOperationException"/>),
    /// sets <paramref name="shuttingDownResponse"/> to a failure response and returns false.
    /// Returns true if the invoke completed without a shutdown exception.
    /// </summary>
    public bool TryInvoke(Action action, out IpcResponse? shuttingDownResponse)
    {
        try
        {
            uiThreadInvoker.Invoke(action);
            shuttingDownResponse = null;
            return true;
        }
        catch (ObjectDisposedException)
        {
            shuttingDownResponse = ShuttingDownResponse;
            return false;
        }
        catch (InvalidOperationException)
        {
            shuttingDownResponse = ShuttingDownResponse;
            return false;
        }
    }

    /// <summary>
    /// Begins an async invoke on the UI thread (fire-and-forget).
    /// Returns false if the application is shutting down (disposed form).
    /// </summary>
    public bool TryBeginInvoke(Action action)
    {
        try { uiThreadInvoker.BeginInvoke(action); return true; }
        catch { return false; }
    }

    /// <summary>
    /// Checks <see cref="IAppStateProvider.IsShuttingDown"/> and returns a shutdown response if true.
    /// </summary>
    public bool IsShuttingDown(out IpcResponse? response)
    {
        if (appState.IsShuttingDown)
        {
            response = ShuttingDownResponse;
            return true;
        }

        response = null;
        return false;
    }
}
