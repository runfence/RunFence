using System.Diagnostics;
using System.Security.Cryptography;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Persistence;
using RunFence.Security;
using RunFence.Startup.UI.Forms;

namespace RunFence.Startup.UI;

public class LockManager(
    SessionContext session,
    IPinService pinService,
    IDatabaseService databaseService,
    ILoggingService log,
    ISecureDesktopRunner secureDesktop,
    IAppInitializationHelper appInit,
    IWindowsHelloService windowsHello)
    : IDisposable
{
    private readonly AutoLockTimerService _autoLockTimerService = new();

    private volatile bool _isLocked;
    private volatile bool _unlockPolling;
    private int _unlockInProgress;

    public event Action? ShowWindowRequested;
    public event Action? ShowWindowUnlockedRequested;
    public event Func<bool>? WindowsHelloUnavailableConfirmRequested;
    public event Func<bool>? WindowsHelloFailedConfirmRequested;
    public event Action<ProtectedBuffer, ProtectedBuffer>? PinResetCompleted;

    public bool IsLocked => _isLocked;
    public bool IsUnlockPolling => _unlockPolling;
    public bool IsUnlocking { get; private set; }

    public void LockWindow()
    {
        _isLocked = true;
        log.Info("Window locked");
    }

    public void TryShowWindow()
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
                LaunchUnlockProcess();
                break;
            case UnlockMode.Pin:
                _unlockPolling = true;
                try
                {
                    PromptPinForUnlock();
                }
                finally
                {
                    _unlockPolling = false;
                }

                break;
            case UnlockMode.WindowsHello:
                _unlockPolling = true;
                try
                {
                    PromptWindowsHelloForUnlock();
                }
                finally
                {
                    _unlockPolling = false;
                }

                break;
        }
    }

    public void Unlock()
    {
        if (session.Database.Settings.UnlockMode == UnlockMode.AdminAndPin)
        {
            bool verified = false;
            var store = session.CredentialStore;
            secureDesktop.Run(() =>
            {
                using var dlg = new PinDialog(PinDialogMode.Verify);
                dlg.VerifyCallback = pin => pinService.VerifyPin(pin, store, out _);
                verified = dlg.ShowDialog() == DialogResult.OK;
            });
            if (!verified)
                return;
        }

        _isLocked = false;
        IsUnlocking = true;
        try
        {
            StopAutoLockTimer();
            ShowWindowUnlockedRequested?.Invoke();
        }
        finally
        {
            IsUnlocking = false;
        }

        log.Info("Window unlocked via IPC");
    }

    public bool TryUnlock(bool isAdmin)
    {
        if (!_isLocked)
            return true;
        if (Interlocked.CompareExchange(ref _unlockInProgress, 1, 0) != 0)
            return false;
        try
        {
            return TryUnlockCore(isAdmin);
        }
        finally
        {
            Interlocked.Exchange(ref _unlockInProgress, 0);
        }
    }

    private bool TryUnlockCore(bool isAdmin)
    {
        if (!_isLocked)
            return true;

        var mode = session.Database.Settings.UnlockMode;
        switch (mode)
        {
            case UnlockMode.Admin when isAdmin:
            case UnlockMode.AdminAndPin when isAdmin:
                Unlock();
                break;
            case UnlockMode.Pin:
                _unlockPolling = true;
                try
                {
                    PromptPinForUnlock();
                }
                finally
                {
                    _unlockPolling = false;
                }

                break;
            case UnlockMode.WindowsHello:
                _unlockPolling = true;
                try
                {
                    PromptWindowsHelloForUnlock();
                }
                finally
                {
                    _unlockPolling = false;
                }

                break;
            case UnlockMode.Admin when !isAdmin:
                LaunchUnlockProcess();
                _unlockPolling = true;
                try
                {
                    var sw = Stopwatch.StartNew();
                    // REENTRANCY WARNING: Application.DoEvents() re-enters the message loop, which means
                    // other UI events (tray clicks, IPC handlers, timer ticks) can execute synchronously
                    // inside this while loop. IsUnlockPolling=true guards IPC handlers from processing
                    // unlock-sensitive operations concurrently, but callers of TryUnlock must be aware
                    // that the call stack can be re-entered before TryUnlock returns.
                    // A full async rewrite (e.g. async/await with TaskCompletionSource) would eliminate
                    // this risk but is out of scope for the current iteration.
                    while (_isLocked && sw.ElapsedMilliseconds < 30_000)
                    {
                        Application.DoEvents();
                        Thread.Sleep(100);
                    }
                }
                finally
                {
                    _unlockPolling = false;
                }

                break;
            case UnlockMode.AdminAndPin when !isAdmin:
                return false;
        }

        return !_isLocked;
    }

    public void StartAutoLockTimer()
    {
        if (!session.Database.Settings.AutoLockOnMinimize)
            return;

        var clampedMinutes = Math.Clamp(session.Database.Settings.AutoLockTimeoutMinutes, 0, 999);
        var timeoutSeconds = clampedMinutes * 60;
        if (timeoutSeconds <= 0)
        {
            _autoLockTimerService.Stop();
            LockWindow();
            return;
        }

        _autoLockTimerService.Start(timeoutSeconds, LockWindow);
    }

    public void StopAutoLockTimer() => _autoLockTimerService.Stop();

    private void LaunchUnlockProcess()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = Constants.UnlockCmdPath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            log.Error("Failed to launch unlock process", ex);
        }
    }

    private void PromptWindowsHelloForUnlock()
    {
        var result = windowsHello.VerifySync("Verify your identity to unlock RunFence");

        switch (result)
        {
            case HelloVerificationResult.Verified:
                session.LastPinVerifiedAt = DateTime.UtcNow;
                log.Info("Unlocked via Windows Hello");
                Unlock();
                break;
            case HelloVerificationResult.Canceled:
                log.Info("Windows Hello verification canceled by user");
                break;
            case HelloVerificationResult.NotAvailable:
                log.Warn("Windows Hello not available for unlock, falling back to PIN");
                if (WindowsHelloUnavailableConfirmRequested?.Invoke() ?? false)
                    PromptPinForUnlock();
                break;
            case HelloVerificationResult.Failed:
                log.Error("Windows Hello verification failed for unlock, falling back to PIN");
                if (WindowsHelloFailedConfirmRequested?.Invoke() ?? false)
                    PromptPinForUnlock();
                break;
        }
    }

    private void PromptPinForUnlock()
    {
        bool verified = false;
        (CredentialStore Store, byte[] Key)? resetResult = null;
        var store = session.CredentialStore;

        secureDesktop.Run(() =>
        {
            using var dlg = new PinDialog(PinDialogMode.Verify);
            dlg.VerifyCallback = pin => pinService.VerifyPin(pin, store, out _);
            var result = dlg.ShowDialog();

            if (result == DialogResult.OK)
            {
                verified = true;
                return;
            }

            if (!dlg.ResetRequested)
                return;

            resetResult = PinResetFlowRunner.RunResetFlow(
                pinService, databaseService, appInit,
                extraStoreInit: store => { appInit.EnsureCurrentAccountCredential(store); });
        });

        if (verified)
        {
            session.LastPinVerifiedAt = DateTime.UtcNow;
            Unlock();
        }
        else if (resetResult is { } r)
        {
            log.Info("PIN reset from unlock dialog");

            var oldBuffer = session.PinDerivedKey;
            var resetDb = new AppDatabase();
            appInit.PopulateDefaultIpcCallers(resetDb);
            session.Database = resetDb;
            session.CredentialStore = r.Store;
            session.PinDerivedKey = new ProtectedBuffer(r.Key);
            CryptographicOperations.ZeroMemory(r.Key);
            session.LastPinVerifiedAt = DateTime.UtcNow;

            PinResetCompleted?.Invoke(oldBuffer, session.PinDerivedKey);
            Unlock();
        }
    }

    public void Dispose()
    {
        _autoLockTimerService.Dispose();
    }
}