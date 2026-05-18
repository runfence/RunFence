using RunFence.Apps.Shortcuts;
using RunFence.Core;
using RunFence.Core.Ipc;
using RunFence.Infrastructure;
using RunFence.Ipc;
using RunFence.Launch;

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
    ShortcutTargetResolver shortcutTargetResolver)
    : IRunAsFlowHandler
{
    private static readonly char[] PathSeparators = ['\\', '/'];

    // Prevents concurrent RunAs operations (0=free, 1=in-progress; set atomically)
    private volatile int _runAsInProgress;

    /// <summary>Returns true if the AppId looks like a file path (contains path separators).</summary>
    public static bool IsRunAsRequest(string appId) => appId.IndexOfAny(PathSeparators) >= 0;

    public void TriggerFromUI(string filePath, string? initialAccountSid = null)
    {
        _ = TriggerFromUIAsync(filePath, initialAccountSid);
    }

    public Task TriggerFromUIAsync(string filePath, string? initialAccountSid = null)
    {
        if (appState.IsShuttingDown || appState.IsModalOpen || appState.IsOperationInProgress)
            return Task.CompletedTask;
        if (Interlocked.CompareExchange(ref _runAsInProgress, 1, 0) != 0)
            return Task.CompletedTask;
        if (dosProtection.IsBlocked())
        {
            Interlocked.Exchange(ref _runAsInProgress, 0);
            return Task.CompletedTask;
        }

        return HandleRunAsOnUIThreadAsync(filePath, null, null, initialAccountSid, isAdmin: true, useSecureDesktop: false);
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
        if (!appLock.IsUnlockPolling && (appState.IsModalOpen || appState.IsOperationInProgress))
            return new IpcResponse { Success = false, ErrorMessage = "Busy." };

        // Atomic claim: prevents TOCTOU race between pipe thread check and UI thread execution
        if (Interlocked.CompareExchange(ref _runAsInProgress, 1, 0) != 0)
            return new IpcResponse { Success = false, ErrorMessage = "Run As already in progress." };

        if (dosProtection.IsBlocked())
        {
            Interlocked.Exchange(ref _runAsInProgress, 0);
            return new IpcResponse { Success = false, ErrorMessage = "Too many requests." };
        }

        // DEFERRED: post to UI thread, return immediately
        try
        {
            uiThreadInvoker.BeginInvoke(() =>
                _ = HandleRunAsOnUIThreadAsync(filePath, message.Arguments, message.WorkingDirectory,
                    initialAccountSid: null, context.IsAdmin, useSecureDesktop: true));
        }
        catch
        {
            Interlocked.Exchange(ref _runAsInProgress, 0);
            return new IpcResponse { Success = false, ErrorMessage = "Shutting down." };
        }

        return new IpcResponse { Success = true };
    }

    private async Task HandleRunAsOnUIThreadAsync(string filePath, string? arguments, string? launcherWorkingDirectory,
        string? initialAccountSid, bool isAdmin, bool useSecureDesktop)
    {
        bool unlockedForRunAs = false;
        try
        {
            if (appState.IsShuttingDown)
                return;
            if (appState.IsModalOpen || appState.IsOperationInProgress)
                return;

            ShortcutContext? shortcutContext = null;
            bool isFolder;

            if (filePath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            {
                var resolved = shortcutTargetResolver.TryResolveShortcut(filePath, appState.Database.Apps);
                if (resolved == null)
                {
                    MessageBox.Show(
                        "Could not resolve shortcut target.\n\nThe shortcut may be broken or reference a removed app entry.",
                        "RunFence", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                shortcutContext = resolved.Value.Context;
                filePath = resolved.Value.ResolvedPath;
                if (!string.IsNullOrEmpty(resolved.Value.ShortcutArgs) && string.IsNullOrEmpty(arguments))
                    arguments = resolved.Value.ShortcutArgs;
                if (!string.IsNullOrEmpty(resolved.Value.ShortcutWorkingDirectory) && string.IsNullOrEmpty(launcherWorkingDirectory))
                    launcherWorkingDirectory = resolved.Value.ShortcutWorkingDirectory;
                isFolder = Directory.Exists(filePath);
            }
            else
            {
                isFolder = Directory.Exists(filePath);
            }

            var originalLnkPath = shortcutContext?.OriginalLnkPath;

            var mainForm = Application.OpenForms.Count > 0 ? Application.OpenForms[0] : null;
            bool restoreMainFormAfterDialog = useSecureDesktop
                && mainForm is { Visible: true, WindowState: not FormWindowState.Minimized };

            using var result = await dialogPresenter.ShowRunAsDialogAsync(filePath, arguments, shortcutContext,
                initialAccountSid, isAdmin, unlocked => unlockedForRunAs = unlocked, useSecureDesktop);

            if (result == null)
                return;

            // After returning from the secure desktop (IPC path), a visible main form loses foreground.
            // Restore only that state; minimized/background requests must stay out of the way.
            if (restoreMainFormAfterDialog && mainForm != null)
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
