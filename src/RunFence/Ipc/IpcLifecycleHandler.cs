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
    IElevatedUnlockRequestHandler elevatedUnlockRequestHandler,
    IOperationUnlockRequestHandler operationUnlockRequestHandler,
    IShowWindowRequestHandler showWindowRequestHandler,
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
    {
        if (!isAdmin)
        {
            log.Warn($"Unlock rejected: caller '{callerIdentity}' is not admin");
            return new IpcResponse { Success = false, ErrorMessage = "Access denied. Admin required." };
        }

        var currentUserSid = SidResolutionHelper.GetCurrentUserSid();
        var allowDirectAdminUnlock = SidComparer.SidEquals(callerSid, currentUserSid);
        log.Info(allowDirectAdminUnlock
            ? $"Unlock requested by {callerIdentity}"
            : $"Unlock requested by {callerIdentity}; using normal unlock flow because caller SID '{callerSid}' does not match current SID '{currentUserSid}'");

        if (ipcUiInvoker.IsShuttingDown(out var shuttingDownResponse))
            return shuttingDownResponse!;

        try
        {
            Task<bool>? unlockTask = null;
            if (!ipcUiInvoker.TryInvoke(
                    () =>
                    {
                        if (allowDirectAdminUnlock)
                            unlockTask = elevatedUnlockRequestHandler.HandleElevatedUnlockRequestAsync();
                        else
                            showWindowRequestHandler.RequestShowWindow();
                    },
                    out var invokeResponse))
                return invokeResponse!;

            if (!allowDirectAdminUnlock)
                return new IpcResponse { Success = true };

            var unlocked = unlockTask!.GetAwaiter().GetResult();
            return unlocked
                ? new IpcResponse { Success = true }
                : new IpcResponse { Success = false, ErrorMessage = "Unlock cancelled." };
        }
        catch (Exception ex)
        {
            log.Error("Unlock failed", ex);
            return new IpcResponse { Success = false, ErrorMessage = "Unlock failed." };
        }
    }

    public IpcResponse HandleUnlockOperation(string? callerIdentity, string? callerSid, bool isAdmin)
    {
        if (!isAdmin)
        {
            log.Warn($"Operation unlock rejected: caller '{callerIdentity}' is not admin");
            return new IpcResponse { Success = false, ErrorMessage = "Access denied. Admin required." };
        }

        var currentUserSid = SidResolutionHelper.GetCurrentUserSid();
        var allowDirectOperationUnlock = SidComparer.SidEquals(callerSid, currentUserSid);
        log.Info(allowDirectOperationUnlock
            ? $"Operation unlock requested by {callerIdentity}"
            : $"Operation unlock requested by {callerIdentity}; using normal operation unlock flow because caller SID '{callerSid}' does not match current SID '{currentUserSid}'");

        if (ipcUiInvoker.IsShuttingDown(out var shuttingDownResponse))
            return shuttingDownResponse!;

        try
        {
            Task<bool>? unlockTask = null;
            if (!ipcUiInvoker.TryInvoke(
                    () =>
                    {
                        if (allowDirectOperationUnlock)
                            unlockTask = operationUnlockRequestHandler.HandleOperationUnlockRequestAsync();
                        else
                            operationUnlockRequestHandler.RequestOperationUnlock();
                    },
                    out var invokeResponse))
                return invokeResponse!;

            if (!allowDirectOperationUnlock)
                return new IpcResponse { Success = true };

            var unlocked = unlockTask!.GetAwaiter().GetResult();
            return unlocked
                ? new IpcResponse { Success = true }
                : new IpcResponse { Success = false, ErrorMessage = "No pending operation unlock." };
        }
        catch (Exception ex)
        {
            log.Error("Operation unlock failed", ex);
            return new IpcResponse { Success = false, ErrorMessage = "Operation unlock failed." };
        }
    }
}
