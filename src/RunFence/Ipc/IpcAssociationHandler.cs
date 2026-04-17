using System.ComponentModel;
using RunFence.Account;
using RunFence.Acl.UI;
using RunFence.Core;
using RunFence.Core.Ipc;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Launch;
using RunFence.Persistence;

namespace RunFence.Ipc;

/// <summary>
/// Handles the <see cref="IpcCommands.HandleAssociation"/> IPC command.
/// Looks up all handler mappings for the association key, selects the app whose
/// <see cref="AppEntry.AllowedIpcCallers"/> explicitly contains the caller (falling back to any
/// authorized app), and launches via the orchestrator.
/// </summary>
public class IpcAssociationHandler(
    IAppStateProvider appState,
    IAppLockControl appLock,
    IpcUiInvoker ipcUiInvoker,
    IAppEntryLauncher entryLauncher,
    IIpcCallerAuthorizer authorizer,
    ISidNameCacheService sidNameCache,
    IHandlerMappingService handlerMappingService,
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
                var database = appState.Database;
                var allMappings = handlerMappingService.GetAllHandlerMappings(database);

                if (!allMappings.TryGetValue(message.Association, out var entries))
                {
                    result = new IpcResponse { Success = false, ErrorCode = IpcErrorCode.UnknownAssociation, ErrorMessage = $"No handler registered for '{message.Association}'." };
                    return;
                }

                // Among all registered apps for this key, prefer the one with an explicit per-app
                // AllowedIpcCallers containing the caller (most specific), then fall back to any authorized app.
                AppEntry? authorizedApp = null;
                bool anyFound = false;
                foreach (var entry in entries)
                {
                    var candidate = database.Apps.FirstOrDefault(a => a.Id == entry.AppId);
                    if (candidate == null) continue;
                    anyFound = true;
                    if (!authorizer.IsCallerAuthorizedForAssociation(context.CallerIdentity, context.CallerSid, candidate, database, context.IdentityFromImpersonation))
                        continue;
                    if (authorizer.HasExplicitPerAppAuthorization(context.CallerSid, candidate, database))
                    {
                        authorizedApp = candidate;
                        break; // explicit per-app match — highest priority
                    }
                    authorizedApp ??= candidate; // keep first authorized as fallback
                }

                if (authorizedApp == null)
                {
                    if (!anyFound)
                    {
                        result = new IpcResponse { Success = false, ErrorMessage = $"Registered app for '{message.Association}' not found (config may be unloaded)." };
                        return;
                    }
                    result = new IpcResponse { Success = false, ErrorCode = IpcErrorCode.AccessDenied, ErrorMessage = "Access denied." };
                    return;
                }

                // Associations intentionally do not fall back to app.ArgumentsTemplate — they use only
                // per-association templates. Null coalesces to "" so DetermineArguments replaces DefaultArguments.
                var associationTemplate = entries
                    .FirstOrDefault(e => string.Equals(e.AppId, authorizedApp.Id, StringComparison.OrdinalIgnoreCase))
                    .ArgumentsTemplate ?? "";

                entryLauncher.Launch(authorizedApp, message.Arguments, message.WorkingDirectory,
                    AclPermissionDialogHelper.CreateLaunchPermissionPrompt(sidNameCache), associationTemplate);
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
                result = new IpcResponse { Success = false, ErrorMessage = "Launch failed." };
            }
        }, out var disposeResponse))
            return disposeResponse!;

        return result ?? new IpcResponse { Success = false, ErrorMessage = "Internal error." };
    }
}
