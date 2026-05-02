using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Security;
using RunFence.Startup.UI.Forms;

namespace RunFence.Startup.UI;

public class LockManager(
    SessionContext session,
    IPinService pinService,
    ILoggingService log,
    ISecureDesktopRunner secureDesktop,
    IWindowsHelloService windowsHello,
    IAutoLockTimerService autoLockTimerService,
    IUnlockProcessLauncher unlockProcessLauncher)
    : ILockManager, ILockUiEventSource
{
    private static readonly TimeSpan OperationUnlockTimeout = TimeSpan.FromMinutes(5);

    private readonly object _operationUnlockLock = new();
    private volatile bool _isLocked;
    private volatile bool _unlockPolling;
    private int _credentialUnlockInProgress;
    private bool _operationUnlockPending;
    private TaskCompletionSource<bool>? _operationUnlockCompletion;

    public event Action? ShowWindowRequested;
    public event Action? ShowWindowUnlockedRequested;
    public event Action? WindowlessUnlockCompleted;
    /// <remarks>Single subscriber expected. If multiple subscribers are needed, change to an event with aggregated result.</remarks>
    public event Func<bool>? WindowsHelloUnavailableConfirmRequested;
    /// <remarks>Single subscriber expected. If multiple subscribers are needed, change to an event with aggregated result.</remarks>
    public event Func<bool>? WindowsHelloFailedConfirmRequested;

    public bool IsLocked => _isLocked;
    public bool IsUnlockPolling => _unlockPolling;
    public bool IsUnlocking { get; private set; }

    public void LockWindow()
    {
        _isLocked = true;
        log.Info("Window locked");
    }

    public async Task TryShowWindowAsync()
    {
        if (!_isLocked)
        {
            StopAutoLockTimer();
            ShowWindowRequested?.Invoke();
            return;
        }

        switch (session.Database.Settings.UnlockMode)
        {
            case UnlockMode.Admin:
            case UnlockMode.AdminAndPin:
                CancelPendingOperationUnlock();
                unlockProcessLauncher.LaunchUnlockProcess(operationUnlock: false);
                break;
            case UnlockMode.Pin:
                await TryUnlockWith(UnlockMode.Pin, showWindow: true);
                break;
            case UnlockMode.WindowsHello:
                await TryUnlockWith(UnlockMode.WindowsHello, showWindow: true);
                break;
        }
    }

    public void Unlock()
    {
        Unlock(showWindow: true);
    }

    private void Unlock(bool showWindow)
    {
        if (showWindow)
            CancelPendingOperationUnlock();

        if (session.Database.Settings.UnlockMode == UnlockMode.AdminAndPin)
        {
            bool verified = false;
            var store = session.CredentialStore;
            secureDesktop.Run(() =>
            {
                using var dlg = new PinDialog(PinDialogMode.Verify);
                dlg.VerifyCallback = (ProtectedString pin) => pinService.VerifyPin(pin, store, out _);
                verified = dlg.ShowDialog() == DialogResult.OK;
            });
            if (!verified)
                return;
        }

        CompleteUnlock("Window unlocked via IPC", showWindow);
    }

    public bool CompletePendingOperationUnlock()
    {
        TaskCompletionSource<bool>? completion;
        lock (_operationUnlockLock)
        {
            if (!_operationUnlockPending)
                return false;

            completion = _operationUnlockCompletion;
            _operationUnlockPending = false;
            _operationUnlockCompletion = null;
            _unlockPolling = false;
        }

        if (!_isLocked)
        {
            completion?.TrySetResult(true);
            return true;
        }

        CompleteUnlock("Window unlocked by pending operation unlock request", showWindow: false);
        completion?.TrySetResult(true);
        return true;
    }

    private void CompleteUnlock(string logMessage, bool showWindow = true)
    {
        _isLocked = false;
        IsUnlocking = true;
        try
        {
            StopAutoLockTimer();
            if (showWindow)
                ShowWindowUnlockedRequested?.Invoke();
            else
                WindowlessUnlockCompleted?.Invoke();
        }
        finally
        {
            IsUnlocking = false;
        }

        log.Info(logMessage);
    }

    public Task<bool> TryUnlockAsync(bool isAdmin) => TryUnlockCoreAsync(isAdmin, showWindow: true, operationUnlock: false);

    public Task<bool> TryUnlockForOperationAsync(bool isAdmin) =>
        TryUnlockCoreAsync(isAdmin, showWindow: false, operationUnlock: true);

    private async Task<bool> TryUnlockCoreAsync(bool isAdmin, bool showWindow, bool operationUnlock)
    {
        if (!_isLocked)
            return true;

        return await TryUnlockModeAsync(isAdmin, showWindow, operationUnlock);
    }

    private async Task<bool> TryUnlockModeAsync(bool isAdmin, bool showWindow, bool operationUnlock)
    {
        if (!_isLocked)
            return true;

        var mode = session.Database.Settings.UnlockMode;
        switch (mode)
        {
            case UnlockMode.Admin when isAdmin:
            case UnlockMode.AdminAndPin when isAdmin:
                Unlock(showWindow);
                break;
            case UnlockMode.Pin:
                return await TryUnlockWith(UnlockMode.Pin, showWindow);
            case UnlockMode.WindowsHello:
                return await TryUnlockWith(UnlockMode.WindowsHello, showWindow);
            case UnlockMode.Admin when !isAdmin:
                if (!operationUnlock)
                {
                    CancelPendingOperationUnlock();
                    unlockProcessLauncher.LaunchUnlockProcess(operationUnlock: false);
                    return false;
                }

                var operationUnlockCompletion = BeginPendingOperationUnlock();
                unlockProcessLauncher.LaunchUnlockProcess(operationUnlock);
                try
                {
                    return await operationUnlockCompletion.Task.WaitAsync(OperationUnlockTimeout);
                }
                catch (TimeoutException)
                {
                    CancelPendingOperationUnlock(operationUnlockCompletion);
                    return false;
                }
            case UnlockMode.AdminAndPin when !isAdmin:
                return false;
        }

        return !_isLocked;
    }

    private TaskCompletionSource<bool> BeginPendingOperationUnlock()
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<bool>? previousCompletion;
        lock (_operationUnlockLock)
        {
            previousCompletion = _operationUnlockCompletion;
            _operationUnlockCompletion = completion;
            _operationUnlockPending = true;
            _unlockPolling = true;
        }

        previousCompletion?.TrySetResult(false);
        return completion;
    }

    private void CancelPendingOperationUnlock(TaskCompletionSource<bool>? expectedCompletion = null)
    {
        TaskCompletionSource<bool>? completion;
        lock (_operationUnlockLock)
        {
            if (expectedCompletion != null && !ReferenceEquals(_operationUnlockCompletion, expectedCompletion))
                return;

            completion = _operationUnlockCompletion;
            _operationUnlockCompletion = null;
            _operationUnlockPending = false;
            _unlockPolling = false;
        }

        completion?.TrySetResult(false);
    }

    public void StartAutoLockTimer(bool immediateOnZero = true, Action? postLockAction = null)
    {
        if (!session.Database.Settings.AutoLockInBackground)
            return;

        var clampedMinutes = Math.Clamp(session.Database.Settings.AutoLockTimeoutMinutes, 0, 999);
        var timeoutSeconds = clampedMinutes * 60;
        if (timeoutSeconds <= 0)
        {
            autoLockTimerService.Stop();
            if (immediateOnZero)
            {
                LockWindow();
                postLockAction?.Invoke();
                return;
            }
            timeoutSeconds = 60;
        }

        autoLockTimerService.Start(timeoutSeconds, () => { LockWindow(); postLockAction?.Invoke(); });
    }

    public void StopAutoLockTimer() => autoLockTimerService.Stop();

    private async Task<bool> TryUnlockWith(UnlockMode mode, bool showWindow)
    {
        if (Interlocked.CompareExchange(ref _credentialUnlockInProgress, 1, 0) != 0)
            return false;

        _unlockPolling = true;
        try
        {
            if (mode == UnlockMode.Pin)
                PromptPinForUnlock(showWindow);
            else
                await PromptWindowsHelloForUnlockAsync(showWindow);

            return !_isLocked;
        }
        finally
        {
            _unlockPolling = false;
            Interlocked.Exchange(ref _credentialUnlockInProgress, 0);
        }
    }

    private async Task PromptWindowsHelloForUnlockAsync(bool showWindow)
    {
        var result = await windowsHello.VerifyAsync("Verify your identity to unlock RunFence");

        switch (result)
        {
            case HelloVerificationResult.Verified:
                session.LastPinVerifiedAt = DateTime.UtcNow;
                log.Info("Unlocked via Windows Hello");
                CompleteUnlock("Window unlocked via Windows Hello", showWindow);
                break;
            case HelloVerificationResult.Canceled:
                log.Info("Windows Hello verification canceled by user");
                break;
            case HelloVerificationResult.NotAvailable:
                log.Warn("Windows Hello not available for unlock, falling back to PIN");
                if (WindowsHelloUnavailableConfirmRequested?.Invoke() ?? false)
                    PromptPinForUnlock(showWindow);
                break;
            case HelloVerificationResult.Failed:
                log.Error("Windows Hello verification failed for unlock, falling back to PIN");
                if (WindowsHelloFailedConfirmRequested?.Invoke() ?? false)
                    PromptPinForUnlock(showWindow);
                break;
        }
    }

    private void PromptPinForUnlock(bool showWindow)
    {
        bool verified = false;
        var store = session.CredentialStore;

        secureDesktop.Run(() =>
        {
            using var dlg = new PinDialog(PinDialogMode.Verify, allowReset: false);
            dlg.VerifyCallback = (ProtectedString pin) => pinService.VerifyPin(pin, store, out _);
            if (dlg.ShowDialog() == DialogResult.OK)
                verified = true;
        });

        if (verified)
        {
            session.LastPinVerifiedAt = DateTime.UtcNow;
            CompleteUnlock("Window unlocked via PIN", showWindow);
        }
    }

}
