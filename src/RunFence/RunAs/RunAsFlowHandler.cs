using RunFence.Core;
using RunFence.Core.Ipc;
using RunFence.Infrastructure;
using RunFence.Ipc;

namespace RunFence.RunAs;

/// <summary>
/// Handles the entire Run As flow: dialog display, account creation, permission grants,
/// app entry persistence, and launch. Extracted from IpcMessageHandler to keep it focused on dispatch.
/// </summary>
public class RunAsFlowHandler(
    IAppStateProvider appState,
    IAppLockControl appLock,
    IUiThreadInvoker uiThreadInvoker,
    ILoggingService log,
    RunAsDialogPresenter dialogPresenter,
    RunAsResultProcessor resultProcessor,
    RunAsDosProtection dosProtection,
    IIpcCallerAuthorizer authorizer,
    IIdleMonitorService idleMonitor,
    RunAsShortcutHelper shortcutHelper)
    : IRunAsFlowHandler
{
    private static readonly char[] PathSeparators = ['\\', '/'];

    // Prevents concurrent RunAs operations (0=free, 1=in-progress; set atomically)
    private volatile int _runAsInProgress;

    /// <summary>Returns true if the AppId looks like a file path (contains path separators).</summary>
    public static bool IsRunAsRequest(string appId) => appId.IndexOfAny(PathSeparators) >= 0;

    public void TriggerFromUI(string filePath)
    {
        if (appState.IsShuttingDown || appLock.IsUnlockPolling || appState.IsModalOpen || appState.IsOperationInProgress)
            return;
        if (Interlocked.CompareExchange(ref _runAsInProgress, 1, 0) != 0)
            return;
        if (dosProtection.IsBlocked())
        {
            Interlocked.Exchange(ref _runAsInProgress, 0);
            return;
        }
        bool isFolder = Directory.Exists(filePath);
        bool fileExists = isFolder || File.Exists(filePath);
        _ = HandleRunAsOnUIThreadAsync(filePath, null, null, isAdmin: true, isFolder, fileExists);
    }

    public IpcResponse HandleRunAs(IpcMessage message, IpcCallerContext context)
    {
        // Path validation and canonicalization
        string filePath;
        try
        {
            filePath = Path.GetFullPath(message.AppId!);
        }
        catch
        {
            return new IpcResponse { Success = false, ErrorMessage = "Invalid path." };
        }

        if (filePath.StartsWith(@"\\", StringComparison.Ordinal))
            return new IpcResponse { Success = false, ErrorMessage = "UNC paths are not supported." };

        // Global caller authorization
        if (!authorizer.IsCallerAuthorizedGlobal(context.CallerIdentity, context.CallerSid, appState.Database, context.IdentityFromImpersonation))
            return new IpcResponse { Success = false, ErrorMessage = "Access denied." };

        // Per-app AllowedIpcCallers is intentionally NOT checked here (global check above still applies).
        // The RunAs path always shows an interactive dialog — the user must manually click Launch to
        // proceed. Unlike the direct Launch IPC path (which triggers a fully automated launch),
        // no app can be launched without explicit user confirmation, so per-app caller restrictions
        // add no meaningful security on this path.

        // Fast rejection checks
        if (appState.IsShuttingDown)
            return new IpcResponse { Success = false, ErrorMessage = "Shutting down." };
        if (appLock.IsUnlockPolling || appState.IsModalOpen || appState.IsOperationInProgress)
            return new IpcResponse { Success = false, ErrorMessage = "Busy." };

        // Atomic claim: prevents TOCTOU race between pipe thread check and UI thread execution
        if (Interlocked.CompareExchange(ref _runAsInProgress, 1, 0) != 0)
            return new IpcResponse { Success = false, ErrorMessage = "Run As already in progress." };

        if (dosProtection.IsBlocked())
        {
            Interlocked.Exchange(ref _runAsInProgress, 0);
            return new IpcResponse { Success = false, ErrorMessage = "Too many requests." };
        }

        // IO check on pipe thread (before BeginInvoke) to avoid blocking UI thread with filesystem calls.
        bool isFolder = Directory.Exists(filePath);
        bool fileExists = isFolder || File.Exists(filePath);

        // DEFERRED: post to UI thread, return immediately
        try
        {
            uiThreadInvoker.BeginInvoke(() =>
                _ = HandleRunAsOnUIThreadAsync(filePath, message.Arguments, message.WorkingDirectory, context.IsAdmin, isFolder, fileExists));
        }
        catch
        {
            Interlocked.Exchange(ref _runAsInProgress, 0);
            return new IpcResponse { Success = false, ErrorMessage = "Shutting down." };
        }

        return new IpcResponse { Success = true };
    }

    private async Task HandleRunAsOnUIThreadAsync(string filePath, string? arguments, string? launcherWorkingDirectory,
        bool isAdmin, bool isFolder, bool fileExists)
    {
        bool unlockedForRunAs = false;
        try
        {
            var originalPath = filePath;
            if (!shortcutHelper.TryHandleLnkPath(ref filePath, ref arguments,
                    out var originalLnkPath, out var shortcutContext, appState.Database.Apps))
                return;

            // If TryHandleLnkPath resolved a .lnk to a different target, recheck existence on the resolved path.
            if (!string.Equals(filePath, originalPath, StringComparison.OrdinalIgnoreCase))
            {
                isFolder = Directory.Exists(filePath);
                fileExists = isFolder || File.Exists(filePath);
            }

            if (!fileExists)
            {
                MessageBox.Show($"Path not found:\n{filePath}", "RunFence",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            using var result = await dialogPresenter.ShowRunAsDialogAsync(filePath, arguments, shortcutContext, isAdmin,
                unlocked => unlockedForRunAs = unlocked);

            if (result == null)
                return;

            // After returning from the secure desktop, the main form loses foreground.
            // Restore it so any subsequent MessageBox.Show dialogs appear in front.
            var mainForm = Application.OpenForms.Count > 0 ? Application.OpenForms[0] : null;
            if (mainForm != null)
            {
                WindowForegroundHelper.ForceToForeground(mainForm.Handle);
                mainForm.BringToFront();
            }

            if (result.RevertShortcutRequested && shortcutContext is { ManagedApp: not null })
            {
                try
                {
                    resultProcessor.ProcessShortcutRevert(originalLnkPath!, shortcutContext.ManagedApp);
                }
                catch (Exception ex)
                {
                    log.Error("Failed to revert shortcut", ex);
                    MessageBox.Show($"Failed to revert shortcut: {ex.Message}", "RunFence",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }

                return;
            }

            if (result.SelectedContainer != null)
            {
                resultProcessor.ProcessContainerResult(result, filePath, arguments, launcherWorkingDirectory, isFolder, originalLnkPath);
                return;
            }

            if (result.Credential != null)
                resultProcessor.ProcessCredentialResult(result, filePath, arguments, launcherWorkingDirectory, isFolder, originalLnkPath);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"An error occurred: {ex.Message}", "RunFence",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            log.Error("HandleRunAsOnUIThreadAsync failed", ex);
        }
        finally
        {
            Interlocked.Exchange(ref _runAsInProgress, 0);
            idleMonitor.ResetIdleTimer();
            if (unlockedForRunAs)
                appLock.Lock();
        }
    }
}