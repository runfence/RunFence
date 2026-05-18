using System.ComponentModel;
using RunFence.Account;
using RunFence.Acl.UI;
using RunFence.Core;
using RunFence.Core.Ipc;
using RunFence.Infrastructure;
using RunFence.Launch;

namespace RunFence.Ipc;

/// <summary>
/// Handles the <see cref="IpcCommands.HandleAssociation"/> IPC command.
/// Resolves the association to the app selected by the standard authorization and
/// path-prefix rules, then launches via the orchestrator.
/// </summary>
public class IpcAssociationHandler(
    IAppStateProvider appState,
    IAppLockControl appLock,
    IIpcUiInvoker ipcUiInvoker,
    IAppEntryLauncher entryLauncher,
    IAssociationLaunchResolver associationLaunchResolver,
    AssociationAccessDeniedNotifier accessDeniedNotifier,
    ISidNameCacheService sidNameCache,
    ILoggingService log,
    IIdleMonitorService idleMonitor)
{
    public IpcResponse HandleAssociation(IpcMessage message, IpcCallerContext context)
    {
        if (string.IsNullOrEmpty(message.Association))
            return new IpcResponse { Success = false, ErrorMessage = "Association key is required." };

        if (ipcUiInvoker.IsShuttingDown(out var shuttingDown))
            return shuttingDown!;

        if (appLock.IsUnlockPolling || appState.IsModalOpen)
            return new IpcResponse { Success = false, ErrorMessage = "Busy." };

        IpcResponse? result = null;
        if (!ipcUiInvoker.TryInvoke(() =>
        {
            try
            {
                var resolution = associationLaunchResolver.Resolve(
                    appState.Database,
                    AssociationLaunchResolver.BuildRequest(message.Association, message.Arguments),
                    context.CallerIdentity,
                    context.CallerSid,
                    context.IdentityFromImpersonation);

                if (resolution.Status != AssociationLaunchResolutionStatus.Success || resolution.App == null)
                {
                    result = resolution.Status switch
                    {
                        AssociationLaunchResolutionStatus.UnknownAssociation => new IpcResponse
                        {
                            Success = false,
                            ErrorCode = IpcErrorCode.UnknownAssociation,
                            ErrorMessage = $"No handler registered for '{message.Association}'."
                        },
                        AssociationLaunchResolutionStatus.AppNotFound => new IpcResponse
                        {
                            Success = false,
                            ErrorCode = IpcErrorCode.AppNotFound,
                            ErrorMessage = $"Registered app for '{message.Association}' not found (config may be unloaded)."
                        },
                        AssociationLaunchResolutionStatus.AccessDenied => new IpcResponse
                        {
                            Success = false,
                            ErrorCode = IpcErrorCode.AccessDenied,
                            ErrorMessage = "Access denied."
                        },
                        AssociationLaunchResolutionStatus.PathPrefixMismatch => new IpcResponse
                        {
                            Success = false,
                            ErrorCode = IpcErrorCode.PathPrefixMismatch,
                            ErrorMessage = "No handler registered for path."
                        },
                        _ => new IpcResponse { Success = false, ErrorMessage = "Launch failed." }
                    };
                    if (resolution.Status == AssociationLaunchResolutionStatus.AccessDenied)
                        accessDeniedNotifier.Notify();
                    return;
                }

                // Associations intentionally do not fall back to app.ArgumentsTemplate â€” they use only
                // per-association templates. Null coalesces to "" so DetermineArguments replaces DefaultArguments.
                var associationTemplate = resolution.Entry?.ArgumentsTemplate ?? string.Empty;

                using var launch = entryLauncher.Launch(
                    resolution.App,
                    message.Arguments,
                    message.WorkingDirectory,
                    AclPermissionDialogHelper.CreateLaunchPermissionPrompt(sidNameCache),
                    associationTemplate);
                var warning = LaunchExecutionWarningFormatter.Format(resolution.App.Name, launch);
                if (warning != null)
                    log.Warn(warning);
                result = new IpcResponse { Success = true, WarningMessage = warning };

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
                result = new IpcResponse { Success = false, ErrorMessage = $"Launch failed: {ex.GetType().Name}" };
            }
        }, out var disposeResponse))
            return disposeResponse!;

        return result ?? new IpcResponse { Success = false, ErrorMessage = "Internal error." };
    }
}
