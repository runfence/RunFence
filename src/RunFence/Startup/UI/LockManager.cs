using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;

namespace RunFence.Startup.UI;

public class LockManager(
    SessionContext session,
    ILoggingService log,
    IAutoLockTimerService autoLockTimerService,
    IUnlockProcessLauncher unlockProcessLauncher,
    ILockStateService lockState,
    ICredentialUnlockService credentialUnlockService,
    IUiThreadInvoker uiThreadInvoker,
    TimeSpan operationUnlockTimeout)
    : ILockManager, ILockUiEventSource
{
    private readonly object _operationUnlockLock = new();
    private volatile bool _unlockPolling;
    private int _credentialUnlockInProgress;
    private bool _operationUnlockPending;
    private TaskCompletionSource<bool>? _operationUnlockCompletion;

    public event Action? ShowWindowRequested;
    public event Action? ShowWindowUnlockedRequested;
    public event Action? WindowlessUnlockCompleted;

    public bool IsLocked => lockState.IsLocked;
    public bool IsUnlockPolling => _unlockPolling;
    public bool IsUnlocking { get; private set; }

    public void LockWindow()
    {
        lockState.Lock();
        log.Info("Window locked");
    }

    public async Task TryShowWindowAsync()
    {
        if (!IsLocked)
        {
            StopAutoLockTimer();
            ShowWindowRequested?.Invoke();
            return;
        }

        await TryUnlockCoreAsync(isAdmin: false, showWindow: true, operationUnlock: false);
    }

    public void Unlock() => Unlock(showWindow: true);

    private void Unlock(bool showWindow)
    {
        if (showWindow)
            CancelPendingOperationUnlock();

        if (session.Database.Settings.UnlockMode == UnlockMode.AdminAndPin &&
            credentialUnlockService.VerifyPin() != CredentialUnlockResult.Succeeded)
            return;

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

        if (!IsLocked)
        {
            completion?.TrySetResult(true);
            return true;
        }

        CompleteUnlock("Window unlocked by pending operation unlock request", showWindow: false);
        completion?.TrySetResult(true);
        return true;
    }

    private void CompleteUnlock(string logMessage, bool showWindow)
    {
        lockState.Unlock();
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

    public async Task<bool> TryUnlockAsync(bool isAdmin) =>
        await TryUnlockCoreAsync(isAdmin, showWindow: true, operationUnlock: false) == OperationUnlockResult.Succeeded;

    public async Task<bool> TryUnlockForOperationAsync(bool isAdmin) =>
        await TryUnlockForOperationWithResultAsync(isAdmin) == OperationUnlockResult.Succeeded;

    public Task<OperationUnlockResult> TryUnlockForOperationWithResultAsync(bool isAdmin) =>
        TryUnlockCoreAsync(isAdmin, showWindow: false, operationUnlock: true);

    private async Task<OperationUnlockResult> TryUnlockCoreAsync(bool isAdmin, bool showWindow, bool operationUnlock)
    {
        if (!IsLocked)
            return OperationUnlockResult.Succeeded;

        var configuredMode = session.Database.Settings.UnlockMode;
        if (configuredMode == UnlockMode.Admin && isAdmin)
        {
            Unlock(showWindow);
            return IsLocked ? OperationUnlockResult.Unavailable : OperationUnlockResult.Succeeded;
        }

        if (configuredMode == UnlockMode.Admin || (configuredMode == UnlockMode.AdminAndPin && !isAdmin))
        {
            if (!operationUnlock)
            {
                CancelPendingOperationUnlock();
                unlockProcessLauncher.LaunchUnlockProcess(operationUnlock: false);
                return OperationUnlockResult.Unavailable;
            }

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

            try
            {
                unlockProcessLauncher.LaunchUnlockProcess(operationUnlock: true);
            }
            catch
            {
                CancelPendingOperationUnlock(completion);
                throw;
            }

            try
            {
                return await completion.Task.WaitAsync(operationUnlockTimeout)
                    ? OperationUnlockResult.Succeeded
                    : OperationUnlockResult.Unavailable;
            }
            catch (TimeoutException)
            {
                CancelPendingOperationUnlock(completion);
                return OperationUnlockResult.Unavailable;
            }
        }

        CredentialUnlockMode credentialMode;
        if (configuredMode == UnlockMode.Pin || configuredMode == UnlockMode.AdminAndPin)
            credentialMode = CredentialUnlockMode.Pin;
        else if (configuredMode == UnlockMode.WindowsHello)
            credentialMode = CredentialUnlockMode.WindowsHelloThenPin;
        else
            return OperationUnlockResult.Unavailable;

        if (Interlocked.CompareExchange(ref _credentialUnlockInProgress, 1, 0) != 0)
            return OperationUnlockResult.Unavailable;

        _unlockPolling = true;
        try
        {
            var result = await credentialUnlockService.VerifyAsync(credentialMode).ConfigureAwait(false);

            if (result != CredentialUnlockResult.Succeeded)
            {
                return result switch
                {
                    CredentialUnlockResult.Canceled => OperationUnlockResult.Declined,
                    CredentialUnlockResult.Failed => OperationUnlockResult.Failed,
                    _ => OperationUnlockResult.Unavailable
                };
            }

            var unlocked = uiThreadInvoker.Invoke(() =>
            {
                session.LastPinVerifiedAt = DateTime.UtcNow;
                CompleteUnlock("Window unlocked", showWindow);
                return true;
            });
            return unlocked ? OperationUnlockResult.Succeeded : OperationUnlockResult.Unavailable;
        }
        finally
        {
            _unlockPolling = false;
            Interlocked.Exchange(ref _credentialUnlockInProgress, 0);
        }
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

    public void StartAutoLockTimer(bool immediateOnZero = true, Action? onTimeout = null, int? timeoutOverrideSeconds = null)
    {
        if (!lockState.AutoLockEnabled)
            return;

        var timeoutSeconds = timeoutOverrideSeconds ?? Math.Clamp(session.Database.Settings.AutoLockTimeoutMinutes, 0, 999) * 60;

        if (timeoutSeconds <= 0)
        {
            if (immediateOnZero)
            {
                autoLockTimerService.Stop();
                ExecuteAutoLockTimeout(onTimeout);
                return;
            }

            timeoutSeconds = 60;
        }

        autoLockTimerService.Start(timeoutSeconds, () => ExecuteAutoLockTimeout(onTimeout));
    }

    public void StopAutoLockTimer() => autoLockTimerService.Stop();

    private void ExecuteAutoLockTimeout(Action? onTimeout)
    {
        if (onTimeout != null)
        {
            onTimeout();
            return;
        }

        LockWindow();
    }
}
