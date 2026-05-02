using RunFence.Core.Ipc;

namespace RunFence.Ipc;

/// <summary>
/// Wraps UI thread invocation with shutdown-safe error handling for IPC handlers.
/// </summary>
public interface IIpcUiInvoker
{
    /// <summary>
    /// Invokes <paramref name="action"/> on the UI thread. If the application is shutting down,
    /// sets <paramref name="shuttingDownResponse"/> to a failure response and returns false.
    /// </summary>
    bool TryInvoke(Action action, out IpcResponse? shuttingDownResponse);

    /// <summary>
    /// Begins an async invoke on the UI thread (fire-and-forget).
    /// Returns false if the application is shutting down.
    /// </summary>
    bool TryBeginInvoke(Action action);

    /// <summary>
    /// Checks whether the application is shutting down and returns a shutdown response if true.
    /// </summary>
    bool IsShuttingDown(out IpcResponse? response);
}
