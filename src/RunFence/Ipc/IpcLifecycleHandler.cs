using RunFence.Core;
using RunFence.Core.Helpers;
using RunFence.Core.Ipc;
using RunFence.Infrastructure;

namespace RunFence.Ipc;

/// <summary>
/// Handles IPC commands that control the application lifecycle: Shutdown and Unlock.
/// </summary>
public class IpcLifecycleHandler(
    IAppStateProvider appState,
    IAppLockControl appLock,
    IIpcUiInvoker ipcUiInvoker,
    IpcUnlockRequestFlow unlockRequestFlow,
    ILoggingService log)
{
    public IpcResponse HandleShutdown(string? callerIdentity, bool isAdmin)
    {
        if (!isAdmin)
        {
            log.Warn($"Shutdown rejected: caller '{callerIdentity}' is not admin");
            return new IpcResponse { Success = false, ErrorMessage = "Access denied. Admin required." };
        }

        if (appState.IsOperationInProgress)
        {
            return new IpcResponse { Success = false, ErrorMessage = "Operation in progress, try again later." };
        }

        if (appLock.IsUnlockPolling)
        {
            log.Warn($"Shutdown rejected: unlock polling in progress (caller '{callerIdentity}')");
            return new IpcResponse { Success = false, ErrorMessage = "Unlock in progress, try again." };
        }

        log.Info($"Shutdown requested by {callerIdentity}");

        // Use BeginInvoke (async post) to avoid deadlock: Invoke would block the pipe
        // thread while Application.Exit triggers FormClosing which may wait for pipe task.
        // Ignore shutdown exceptions here — best-effort; form may already be disposed.
        ipcUiInvoker.TryBeginInvoke(() => Application.Exit());

        return new IpcResponse { Success = true };
    }

    public IpcResponse HandleUnlockApp(string? callerIdentity, string? callerSid, bool isAdmin)
        => unlockRequestFlow.HandleUnlockApp(
            new IpcOperationRequest(callerIdentity, callerSid, isAdmin),
            CancellationToken.None);

    public IpcResponse HandleUnlockOperation(string? callerIdentity, string? callerSid, bool isAdmin)
        => unlockRequestFlow.HandleUnlockOperation(
            new IpcOperationRequest(callerIdentity, callerSid, isAdmin),
            CancellationToken.None);
}
