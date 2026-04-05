using RunFence.Core;
using RunFence.Core.Ipc;
using RunFence.Infrastructure;
using RunFence.Persistence.UI;

namespace RunFence.Ipc;

public class IpcMessageHandler : IIpcMessageHandler
{
    private readonly IAppStateProvider _appState;
    private readonly IAppLockControl _appLock;
    private readonly IUiThreadInvoker _uiThreadInvoker;
    private readonly ILoggingService _log;
    private readonly IIdleMonitorService _idleMonitor;
    private readonly IConfigManagementContext? _configContext;
    private readonly IpcLaunchHandler _launchHandler;
    private readonly IpcOpenFolderHandler _openFolderHandler;
    private readonly IpcAssociationHandler? _associationHandler;

    public IpcMessageHandler(
        IAppStateProvider appState,
        IAppLockControl appLock,
        IUiThreadInvoker uiThreadInvoker,
        ILoggingService log,
        IpcLaunchHandler launchHandler,
        IpcOpenFolderHandler openFolderHandler,
        IIdleMonitorService idleMonitor,
        IConfigManagementContext? configContext = null,
        IpcAssociationHandler? associationHandler = null)
    {
        _appState = appState;
        _appLock = appLock;
        _uiThreadInvoker = uiThreadInvoker;
        _log = log;
        _idleMonitor = idleMonitor;
        _configContext = configContext;
        _launchHandler = launchHandler;
        _openFolderHandler = openFolderHandler;
        _associationHandler = associationHandler;
    }

    public IpcResponse HandleIpcMessage(IpcMessage message, string? callerIdentity, string? callerSid, bool isAdmin)
    {
        try
        {
            switch (message.Command)
            {
                case IpcCommands.Ping:
                    return new IpcResponse { Success = true };

                case IpcCommands.Shutdown:
                    return HandleShutdown(callerIdentity, isAdmin);

                case IpcCommands.Unlock:
                    return HandleUnlock(callerIdentity, isAdmin);

                case IpcCommands.LoadApps:
                    return HandleLoadApps(callerIdentity, isAdmin, message);

                case IpcCommands.UnloadApps:
                    return HandleUnloadApps(callerIdentity, isAdmin, message);

                case IpcCommands.OpenFolder:
                    return _openFolderHandler.HandleOpenFolder(message, callerIdentity, callerSid);

                case IpcCommands.Launch:
                    return _launchHandler.HandleLaunch(message, callerIdentity, callerSid, isAdmin);

                case IpcCommands.HandleAssociation:
                    if (_associationHandler == null)
                        return new IpcResponse { Success = false, ErrorMessage = "Association handling not available." };
                    return _associationHandler.HandleAssociation(message, callerIdentity, callerSid);

                default:
                    return new IpcResponse { Success = false, ErrorMessage = $"Unknown command: {message.Command}" };
            }
        }
        catch (Exception ex)
        {
            _log.Error("IPC handler error", ex);
            return new IpcResponse { Success = false, ErrorMessage = "Internal error." };
        }
    }

    private IpcResponse HandleShutdown(string? callerIdentity, bool isAdmin)
    {
        if (!isAdmin)
        {
            _log.Warn($"Shutdown rejected: caller '{callerIdentity}' is not admin");
            return new IpcResponse { Success = false, ErrorMessage = "Access denied. Admin required." };
        }

        if (_appState.IsOperationInProgress)
        {
            return new IpcResponse { Success = false, ErrorMessage = "Operation in progress, try again later." };
        }

        if (_appLock.IsUnlockPolling)
        {
            _log.Warn($"Shutdown rejected: unlock polling in progress (caller '{callerIdentity}')");
            return new IpcResponse { Success = false, ErrorMessage = "Unlock in progress, try again." };
        }

        _log.Info($"Shutdown requested by {callerIdentity}");

        // Use BeginInvoke (async post) to avoid deadlock: Invoke would block the pipe
        // thread while Application.Exit triggers FormClosing which may wait for pipe task
        try
        {
            _uiThreadInvoker.BeginInvoke(() => Application.Exit());
        }
        catch
        {
        } // best-effort; form may already be disposed

        return new IpcResponse { Success = true };
    }

    private IpcResponse HandleUnlock(string? callerIdentity, bool isAdmin)
    {
        if (!isAdmin)
        {
            _log.Warn($"Unlock rejected: caller '{callerIdentity}' is not admin");
            return new IpcResponse { Success = false, ErrorMessage = "Access denied. Admin required." };
        }

        _log.Info($"Unlock requested by {callerIdentity}");

        bool unlocked = false;
        try
        {
            _uiThreadInvoker.Invoke(() =>
            {
                _appLock.Unlock();
                unlocked = !_appLock.IsLocked;
            });
        }
        catch (Exception ex)
        {
            _log.Error("Unlock failed", ex);
            return new IpcResponse { Success = false, ErrorMessage = "Unlock failed." };
        }

        return unlocked
            ? new IpcResponse { Success = true }
            : new IpcResponse { Success = false, ErrorMessage = "Unlock cancelled." };
    }

    private IpcResponse HandleLoadApps(string? callerIdentity, bool isAdmin, IpcMessage message)
        => HandleConfigOperation(callerIdentity, isAdmin, "LoadApps", message, path =>
        {
            var (success, error) = _configContext!.LoadApps(path);
            return (success, success ? null : error ?? "Failed to load apps.");
        });

    private IpcResponse HandleUnloadApps(string? callerIdentity, bool isAdmin, IpcMessage message)
        => HandleConfigOperation(callerIdentity, isAdmin, "UnloadApps", message, path =>
        {
            var success = _configContext!.UnloadApps(path);
            return (success, success ? null : "Failed to unload apps.");
        });

    private IpcResponse HandleConfigOperation(string? callerIdentity, bool isAdmin, string operationName,
        IpcMessage message, Func<string, (bool success, string? error)> operation)
    {
        if (!isAdmin)
        {
            _log.Warn($"{operationName} rejected: caller '{callerIdentity}' is not admin");
            return new IpcResponse { Success = false, ErrorMessage = "Access denied. Admin required." };
        }

        if (_configContext == null)
            return new IpcResponse { Success = false, ErrorMessage = "Config management not available." };

        var path = message.Arguments;
        if (string.IsNullOrEmpty(path))
            return new IpcResponse { Success = false, ErrorMessage = "Config path is required." };

        if (_appState.IsShuttingDown)
            return new IpcResponse { Success = false, ErrorMessage = "Application is shutting down." };

        if (_appState.IsModalOpen || _appState.IsOperationInProgress)
            return new IpcResponse { Success = false, ErrorMessage = "Operation in progress, try again later." };

        IpcResponse? result = null;
        try
        {
            _uiThreadInvoker.Invoke(() =>
            {
                // Auto-unlock before config operation; if unlock fails (e.g. PIN cancelled), abort
                _appLock.Unlock();
                if (_appLock.IsLocked)
                {
                    result = new IpcResponse { Success = false, ErrorMessage = "Unlock required." };
                    return;
                }

                var (success, error) = operation(path);
                result = success
                    ? new IpcResponse { Success = true }
                    : new IpcResponse { Success = false, ErrorMessage = error };

                _idleMonitor.ResetIdleTimer();
            });
        }
        catch (ObjectDisposedException)
        {
            return new IpcResponse { Success = false, ErrorMessage = "Application is shutting down." };
        }
        catch (InvalidOperationException)
        {
            return new IpcResponse { Success = false, ErrorMessage = "Application is shutting down." };
        }

        return result ?? new IpcResponse { Success = false, ErrorMessage = "Internal error." };
    }
}