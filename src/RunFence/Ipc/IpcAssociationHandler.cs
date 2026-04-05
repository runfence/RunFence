using System.ComponentModel;
using RunFence.Core;
using RunFence.Core.Ipc;
using RunFence.Infrastructure;
using RunFence.Launch;
using RunFence.Persistence;

namespace RunFence.Ipc;

/// <summary>
/// Handles the <see cref="IpcCommands.HandleAssociation"/> IPC command.
/// Looks up the effective handler mapping for the association key, finds the target app entry,
/// authorizes the caller (always allowing the interactive user), and launches via the orchestrator.
/// </summary>
public class IpcAssociationHandler(
    IAppStateProvider appState,
    IAppLockControl appLock,
    IUiThreadInvoker uiThreadInvoker,
    IAppLaunchOrchestrator launchOrchestrator,
    IIpcCallerAuthorizer authorizer,
    IHandlerMappingService handlerMappingService,
    ILoggingService log,
    IIdleMonitorService idleMonitor)
{
    public IpcResponse HandleAssociation(IpcMessage message, string? callerIdentity, string? callerSid)
    {
        if (string.IsNullOrEmpty(message.Association))
            return new IpcResponse { Success = false, ErrorMessage = "Association key is required." };

        if (appState.IsShuttingDown)
            return new IpcResponse { Success = false, ErrorMessage = "Application is shutting down." };

        if (appLock.IsUnlockPolling)
            return new IpcResponse { Success = false, ErrorMessage = "Busy." };

        IpcResponse? result = null;
        try
        {
            uiThreadInvoker.Invoke(() =>
            {
                try
                {
                    var database = appState.Database;
                    var effectiveMappings = handlerMappingService.GetEffectiveHandlerMappings(database);

                    if (!effectiveMappings.TryGetValue(message.Association, out var appId))
                    {
                        result = new IpcResponse { Success = false, ErrorMessage = $"No handler registered for '{message.Association}'." };
                        return;
                    }

                    var app = database.Apps.FirstOrDefault(a => a.Id == appId);
                    if (app == null)
                    {
                        result = new IpcResponse { Success = false, ErrorMessage = $"Registered app '{appId}' not found (config may be unloaded)." };
                        return;
                    }

                    if (!authorizer.IsCallerAuthorizedForAssociation(callerIdentity, callerSid, app, database))
                    {
                        result = new IpcResponse { Success = false, ErrorMessage = "Access denied." };
                        return;
                    }

                    launchOrchestrator.Launch(app, message.Arguments, message.WorkingDirectory);
                    result = new IpcResponse { Success = true };

                    idleMonitor.ResetIdleTimer();
                }
                catch (Win32Exception ex) when (ex.NativeErrorCode == ProcessLaunchNative.Win32ErrorLogonFailure)
                {
                    result = new IpcResponse
                    {
                        Success = false,
                        ErrorMessage = "Stored credentials are incorrect. Please update the password in RunFence."
                    };
                }
                catch (Exception ex)
                {
                    log.Error("IPC association launch failed", ex);
                    result = new IpcResponse { Success = false, ErrorMessage = ex.Message };
                }
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