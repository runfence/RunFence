using RunFence.Core;
using RunFence.Core.Ipc;
using RunFence.Infrastructure;
using RunFence.Persistence.UI;

namespace RunFence.Ipc;

public class IpcMessageHandler(
    IAppStateProvider appState,
    IAppLockControl appLock,
    IpcUiInvoker ipcUiInvoker,
    ILoggingService log,
    IpcLaunchHandler launchHandler,
    IpcOpenFolderHandler openFolderHandler,
    IIdleMonitorService idleMonitor,
    IConfigManagementContext? configContext = null,
    IpcAssociationHandler? associationHandler = null)
    : IIpcMessageHandler
{
    public IpcResponse HandleIpcMessage(IpcMessage message, IpcCallerContext context)
    {
        try
        {
            switch (message.Command)
            {
                case IpcCommands.Ping:
                    return new IpcResponse { Success = true };

                case IpcCommands.Shutdown:
                    return HandleShutdown(context.CallerIdentity, context.IsAdmin);

                case IpcCommands.Unlock:
                    return HandleUnlock(context.CallerIdentity, context.IsAdmin);

                case IpcCommands.LoadApps:
                    return HandleLoadApps(context.CallerIdentity, context.IsAdmin, message);

                case IpcCommands.UnloadApps:
                    return HandleUnloadApps(context.CallerIdentity, context.IsAdmin, message);

                case IpcCommands.OpenFolder:
                    return openFolderHandler.HandleOpenFolder(message, context.CallerIdentity, context.CallerSid);

                case IpcCommands.Launch:
                    return launchHandler.HandleLaunch(message, context);

                case IpcCommands.HandleAssociation:
                    if (associationHandler == null)
                        return new IpcResponse { Success = false, ErrorMessage = "Association handling not available." };
                    return associationHandler.HandleAssociation(message, context);

                default:
                    return new IpcResponse { Success = false, ErrorMessage = $"Unknown command: {message.Command}" };
            }
        }
        catch (Exception ex)
        {
            log.Error("IPC handler error", ex);
            return new IpcResponse { Success = false, ErrorMessage = "Internal error." };
        }
    }

    private IpcResponse HandleShutdown(string? callerIdentity, bool isAdmin)
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

    private IpcResponse HandleUnlock(string? callerIdentity, bool isAdmin)
    {
        if (!isAdmin)
        {
            log.Warn($"Unlock rejected: caller '{callerIdentity}' is not admin");
            return new IpcResponse { Success = false, ErrorMessage = "Access denied. Admin required." };
        }

        log.Info($"Unlock requested by {callerIdentity}");

        bool unlocked = false;
        try
        {
            if (!ipcUiInvoker.TryInvoke(() => { appLock.Unlock(); unlocked = !appLock.IsLocked; }, out var shutdownResponse))
                return shutdownResponse!;
        }
        catch (Exception ex)
        {
            log.Error("Unlock failed", ex);
            return new IpcResponse { Success = false, ErrorMessage = "Unlock failed." };
        }

        return unlocked
            ? new IpcResponse { Success = true }
            : new IpcResponse { Success = false, ErrorMessage = "Unlock cancelled." };
    }

    private IpcResponse HandleLoadApps(string? callerIdentity, bool isAdmin, IpcMessage message)
        => HandleConfigOperation(callerIdentity, isAdmin, "LoadApps", message, path =>
        {
            var (success, error) = configContext!.LoadApps(path);
            return (success, success ? null : error ?? "Failed to load apps.");
        });

    private IpcResponse HandleUnloadApps(string? callerIdentity, bool isAdmin, IpcMessage message)
        => HandleConfigOperation(callerIdentity, isAdmin, "UnloadApps", message, path =>
        {
            var success = configContext!.UnloadApps(path);
            return (success, success ? null : "Failed to unload apps.");
        });

    private IpcResponse HandleConfigOperation(string? callerIdentity, bool isAdmin, string operationName,
        IpcMessage message, Func<string, (bool success, string? error)> operation)
    {
        if (!isAdmin)
        {
            log.Warn($"{operationName} rejected: caller '{callerIdentity}' is not admin");
            return new IpcResponse { Success = false, ErrorMessage = "Access denied. Admin required." };
        }

        if (configContext == null)
            return new IpcResponse { Success = false, ErrorMessage = "Config management not available." };

        var path = message.Arguments;
        if (string.IsNullOrEmpty(path))
            return new IpcResponse { Success = false, ErrorMessage = "Config path is required." };

        if (ipcUiInvoker.IsShuttingDown(out var shuttingDown))
            return shuttingDown!;

        if (appState.IsModalOpen || appState.IsOperationInProgress)
            return new IpcResponse { Success = false, ErrorMessage = "Operation in progress, try again later." };

        // If the app is locked, refuse immediately rather than blocking the pipe thread
        // with a PIN prompt on the UI thread.
        if (appLock.IsLocked)
            return new IpcResponse { Success = false, ErrorMessage = "App is locked. Unlock first." };

        IpcResponse? result = null;
        if (!ipcUiInvoker.TryInvoke(() =>
        {
            // Re-check lock state on the UI thread; if it changed while we were crossing threads, abort.
            if (appLock.IsLocked)
            {
                result = new IpcResponse { Success = false, ErrorMessage = "App is locked. Unlock first." };
                return;
            }

            var (success, error) = operation(path);
            result = success
                ? new IpcResponse { Success = true }
                : new IpcResponse { Success = false, ErrorMessage = error };

            idleMonitor.ResetIdleTimer();
        }, out var disposeResponse))
            return disposeResponse!;

        return result ?? new IpcResponse { Success = false, ErrorMessage = "Internal error." };
    }
}
