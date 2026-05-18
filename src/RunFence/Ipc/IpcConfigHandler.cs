using RunFence.Core;
using RunFence.Core.Ipc;
using RunFence.Infrastructure;
using RunFence.Persistence.UI;

namespace RunFence.Ipc;

/// <summary>
/// Handles IPC commands that load or unload additional app configuration files.
/// </summary>
public class IpcConfigHandler(
    IAppStateProvider appState,
    IAppLockControl appLock,
    IIpcUiInvoker ipcUiInvoker,
    IIdleMonitorService idleMonitor,
    ILoggingService log,
    IConfigManagementContext? configContext = null)
{
    public IpcResponse HandleLoadApps(string? callerIdentity, bool isAdmin, IpcMessage message)
        => HandleConfigOperation(callerIdentity, isAdmin, "LoadApps", message, path =>
        {
            var result = configContext!.LoadApps(path);
            return (result.Succeeded, result.Succeeded ? null : result.ErrorMessage ?? "Failed to load apps.");
        });

    public IpcResponse HandleUnloadApps(string? callerIdentity, bool isAdmin, IpcMessage message)
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
