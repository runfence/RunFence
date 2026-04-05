using RunFence.Core;
using RunFence.Core.Ipc;
using RunFence.Infrastructure;
using RunFence.Ipc;

namespace RunFence.RunAs;

/// <summary>
/// Handles the entire Run As flow: dialog display, account creation, permission grants,
/// app entry persistence, and launch. Extracted from IpcMessageHandler to keep it focused on dispatch.
/// </summary>
public class RunAsFlowHandler : IRunAsFlowHandler
{
    private static readonly char[] PathSeparators = ['\\', '/'];

    private readonly IAppStateProvider _appState;
    private readonly IAppLockControl _appLock;
    private readonly IUiThreadInvoker _uiThreadInvoker;
    private readonly ILoggingService _log;
    private readonly IIdleMonitorService _idleMonitor;
    private readonly IIpcCallerAuthorizer _authorizer;
    private readonly RunAsDialogPresenter _dialogPresenter;
    private readonly RunAsResultProcessor _resultProcessor;
    private readonly RunAsDosProtection _dosProtection;

    // Prevents concurrent RunAs operations (0=free, 1=in-progress; set atomically on pipe thread)
    private volatile int _runAsInProgress;

    public RunAsFlowHandler(
        IAppStateProvider appState,
        IAppLockControl appLock,
        IUiThreadInvoker uiThreadInvoker,
        ILoggingService log,
        RunAsDialogPresenter dialogPresenter,
        RunAsResultProcessor resultProcessor,
        RunAsDosProtection dosProtection,
        IIpcCallerAuthorizer authorizer,
        IIdleMonitorService idleMonitor)
    {
        _appState = appState;
        _appLock = appLock;
        _uiThreadInvoker = uiThreadInvoker;
        _log = log;
        _dialogPresenter = dialogPresenter;
        _resultProcessor = resultProcessor;
        _dosProtection = dosProtection;
        _authorizer = authorizer;
        _idleMonitor = idleMonitor;
    }

    /// <summary>Returns true if the AppId looks like a file path (contains path separators).</summary>
    public static bool IsRunAsRequest(string appId) => appId.IndexOfAny(PathSeparators) >= 0;

    public IpcResponse HandleRunAs(IpcMessage message, string? callerIdentity, string? callerSid, bool isAdmin)
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
        if (!_authorizer.IsCallerAuthorizedGlobal(callerIdentity, callerSid, _appState.Database))
            return new IpcResponse { Success = false, ErrorMessage = "Access denied." };

        // Per-app AllowedIpcCallers is intentionally NOT checked here (global check above still applies).
        // The RunAs path always shows an interactive dialog — the user must manually click Launch to
        // proceed. Unlike the direct Launch IPC path (which triggers a fully automated launch),
        // no app can be launched without explicit user confirmation, so per-app caller restrictions
        // add no meaningful security on this path.

        // Fast rejection checks
        if (_appState.IsShuttingDown)
            return new IpcResponse { Success = false, ErrorMessage = "Shutting down." };
        if (_appLock.IsUnlockPolling || _appState.IsModalOpen || _appState.IsOperationInProgress)
            return new IpcResponse { Success = false, ErrorMessage = "Busy." };

        // Atomic claim: prevents TOCTOU race between pipe thread check and UI thread execution
        if (Interlocked.CompareExchange(ref _runAsInProgress, 1, 0) != 0)
            return new IpcResponse { Success = false, ErrorMessage = "Run As already in progress." };

        if (_dosProtection.IsBlocked())
        {
            Interlocked.Exchange(ref _runAsInProgress, 0);
            return new IpcResponse { Success = false, ErrorMessage = "Too many requests." };
        }

        // DEFERRED: post to UI thread, return immediately
        try
        {
            _uiThreadInvoker.BeginInvoke(() =>
                HandleRunAsOnUIThread(filePath, message.Arguments, message.WorkingDirectory, isAdmin));
        }
        catch
        {
            Interlocked.Exchange(ref _runAsInProgress, 0);
            return new IpcResponse { Success = false, ErrorMessage = "Shutting down." };
        }

        return new IpcResponse { Success = true };
    }

    private void HandleRunAsOnUIThread(string filePath, string? arguments, string? launcherWorkingDirectory, bool isAdmin)
    {
        bool unlockedForRunAs = false;
        try
        {
            if (!RunAsShortcutHelper.TryHandleLnkPath(ref filePath, ref arguments,
                    out var originalLnkPath, out var shortcutContext, _appState.Database.Apps))
                return;

            bool isFolder = Directory.Exists(filePath);
            if (!isFolder && !File.Exists(filePath))
            {
                MessageBox.Show($"Path not found:\n{filePath}", "RunFence",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            using var result = _dialogPresenter.ShowRunAsDialog(filePath, arguments, shortcutContext, isAdmin, out unlockedForRunAs);

            if (result == null)
                return;

            // After returning from the secure desktop, the main form loses foreground.
            // Restore it so any subsequent MessageBox.Show dialogs appear in front.
            var mainForm = Application.OpenForms.Count > 0 ? Application.OpenForms[0] : null;
            if (mainForm != null)
                NativeInterop.ForceToForeground(mainForm);

            if (result.RevertShortcutRequested && shortcutContext is { ManagedApp: not null })
            {
                try
                {
                    _resultProcessor.ProcessShortcutRevert(originalLnkPath!, shortcutContext.ManagedApp);
                }
                catch (Exception ex)
                {
                    _log.Error("Failed to revert shortcut", ex);
                    MessageBox.Show($"Failed to revert shortcut: {ex.Message}", "RunFence",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }

                return;
            }

            if (result.SelectedContainer != null)
            {
                _resultProcessor.ProcessContainerResult(result, filePath, arguments, launcherWorkingDirectory, isFolder, originalLnkPath);
                return;
            }

            if (result.Credential != null)
                _resultProcessor.ProcessCredentialResult(result, filePath, arguments, launcherWorkingDirectory, isFolder, originalLnkPath);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"An error occurred: {ex.Message}", "RunFence",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            _log.Error("HandleRunAsOnUIThread failed", ex);
        }
        finally
        {
            Interlocked.Exchange(ref _runAsInProgress, 0);
            _idleMonitor.ResetIdleTimer();
            if (unlockedForRunAs)
                _appLock.Lock();
        }
    }
}