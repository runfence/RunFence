using System.ComponentModel;
using RunFence.Account;
using RunFence.Acl;
using RunFence.Acl.UI;
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
    IIpcUiInvoker ipcUiInvoker,
    IAppEntryLauncher entryLauncher,
    IIpcCallerAuthorizer authorizer,
    ISidNameCacheService sidNameCache,
    IIdleMonitorService idleMonitor,
    ILaunchFeedbackPresenter launchFeedbackPresenter,
    IRunAsFlowHandler? runAsFlowHandler = null)
{
    public IpcResponse HandleLaunch(IpcMessage message, IpcCallerContext context)
    {
        if (string.IsNullOrEmpty(message.AppId))
            return new IpcResponse { Success = false, ErrorMessage = "AppId is required." };

        // Path detection: if AppId contains path separators, treat as RunAs request
        if (RunAsFlowHandler.IsRunAsRequest(message.AppId))
        {
            if (runAsFlowHandler == null)
                return new IpcResponse { Success = false, ErrorMessage = "Run As not available." };
            return runAsFlowHandler.HandleRunAs(message, context);
        }

        if (ipcUiInvoker.IsShuttingDown(out var shuttingDown))
            return shuttingDown!;

        // CRITICAL: IsUnlockPolling guard must stay before Invoke to reject requests during unlock.
        if (appLock.IsUnlockPolling)
            return new IpcResponse { Success = false, ErrorMessage = "Busy." };

        IpcResponse? authResult = null;
        if (!ipcUiInvoker.TryInvoke(() =>
        {
            var app = appState.Database.Apps.FirstOrDefault(a => a.Id == message.AppId);
            if (app == null)
            {
                authResult = new IpcResponse { Success = false, ErrorMessage = "Application not found." };
                return;
            }

            if (!authorizer.IsCallerAuthorized(context.CallerIdentity, context.CallerSid, app, appState.Database, context.IdentityFromImpersonation))
            {
                authResult = new IpcResponse { Success = false, ErrorMessage = "Access denied." };
                return;
            }

            // Launch is fire-and-forget: pipe is freed immediately after auth. The association
            // handler uses synchronous dispatch because it needs to report credential errors to
            // the Launcher for user feedback. Here, errors are logged but not returned to caller.
            var capturedApp = app;
            ipcUiInvoker.TryBeginInvoke(() =>
            {
                try
                {
                    using var launch = entryLauncher.Launch(capturedApp, message.Arguments, message.WorkingDirectory,
                        AclPermissionDialogHelper.CreateLaunchPermissionPrompt(sidNameCache));
                    launchFeedbackPresenter.ShowMaintenanceWarning(launch, new LaunchFeedbackContext(capturedApp.Name, LaunchFeedbackSource.SilentIpc)
                    {
                        SummaryName = capturedApp.Name
                    });
                    idleMonitor.ResetIdleTimer();
                }
                catch (Win32Exception ex) when (ex.NativeErrorCode == ProcessLaunchNative.Win32ErrorLogonFailure)
                {
                    launchFeedbackPresenter.ShowLaunchFailure(
                        "Stored credentials are incorrect. Please update the password in RunFence.",
                        ex,
                        new LaunchFeedbackContext(capturedApp.Name, LaunchFeedbackSource.SilentIpc)
                        {
                            SummaryName = capturedApp.Name,
                            FailureCaption = "Launch Failed"
                        });
                }
                catch (GrantOperationException ex)
                {
                    launchFeedbackPresenter.ShowGrantFailure(ex, new LaunchFeedbackContext(capturedApp.Name, LaunchFeedbackSource.SilentIpc)
                    {
                        SummaryName = capturedApp.Name
                    });
                }
                catch (Exception ex)
                {
                    launchFeedbackPresenter.ShowLaunchFailure(
                        $"Launch failed: {ex.Message}",
                        ex,
                        new LaunchFeedbackContext(capturedApp.Name, LaunchFeedbackSource.SilentIpc)
                        {
                            SummaryName = capturedApp.Name
                        });
                }
            });
        }, out var disposeResponse))
            return disposeResponse!;

        return authResult ?? new IpcResponse { Success = true };
    }
}
