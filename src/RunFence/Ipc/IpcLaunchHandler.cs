using System.ComponentModel;
using RunFence.Core;
using RunFence.Core.Ipc;
using RunFence.Infrastructure;
using RunFence.Launch;
using RunFence.RunAs;

namespace RunFence.Ipc;

/// <summary>
/// Handles the IPC Launch command: validates, authorizes, and executes application launches.
/// Also routes path-based AppIds to the RunAs flow.
/// </summary>
public class IpcLaunchHandler(
    IAppStateProvider appState,
    IAppLockControl appLock,
    IUiThreadInvoker uiThreadInvoker,
    IAppLaunchOrchestrator launchOrchestrator,
    IIpcCallerAuthorizer authorizer,
    ILoggingService log,
    IIdleMonitorService idleMonitor,
    IRunAsFlowHandler? runAsFlowHandler = null)
{
    public IpcResponse HandleLaunch(IpcMessage message, string? callerIdentity, string? callerSid, bool isAdmin)
    {
        if (string.IsNullOrEmpty(message.AppId))
            return new IpcResponse { Success = false, ErrorMessage = "AppId is required." };

        // Path detection: if AppId contains path separators, treat as RunAs request
        if (RunAsFlowHandler.IsRunAsRequest(message.AppId))
        {
            if (runAsFlowHandler == null)
                return new IpcResponse { Success = false, ErrorMessage = "Run As not available." };
            return runAsFlowHandler.HandleRunAs(message, callerIdentity, callerSid, isAdmin);
        }

        if (appState.IsShuttingDown)
            return new IpcResponse { Success = false, ErrorMessage = "Application is shutting down." };

        if (appLock.IsUnlockPolling)
            return new IpcResponse { Success = false, ErrorMessage = "Busy." };

        IpcResponse? launchResult = null;
        try
        {
            uiThreadInvoker.Invoke(() =>
            {
                try
                {
                    var app = appState.Database.Apps.FirstOrDefault(a => a.Id == message.AppId);
                    if (app == null)
                    {
                        launchResult = new IpcResponse { Success = false, ErrorMessage = "Application not found." };
                        return;
                    }

                    if (!authorizer.IsCallerAuthorized(callerIdentity, callerSid, app, appState.Database))
                    {
                        launchResult = new IpcResponse { Success = false, ErrorMessage = "Access denied." };
                        return;
                    }

                    launchOrchestrator.Launch(app, message.Arguments, message.WorkingDirectory);
                    launchResult = new IpcResponse { Success = true };

                    idleMonitor.ResetIdleTimer();
                }
                catch (Win32Exception ex) when (ex.NativeErrorCode == ProcessLaunchNative.Win32ErrorLogonFailure)
                {
                    launchResult = new IpcResponse
                    {
                        Success = false,
                        ErrorMessage = "Stored credentials are incorrect. Please update the password in RunFence."
                    };
                }
                catch (Exception ex)
                {
                    log.Error("IPC launch failed", ex);
                    launchResult = new IpcResponse { Success = false, ErrorMessage = ex.Message };
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

        return launchResult ?? new IpcResponse { Success = false, ErrorMessage = "Internal error." };
    }
}