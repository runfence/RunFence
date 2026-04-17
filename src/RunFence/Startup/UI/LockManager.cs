using System.Diagnostics;
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
    IAutoLockTimerService autoLockTimerService)
    : ILockManager
{
    private volatile bool _isLocked;
    private volatile bool _unlockPolling;
    private int _unlockInProgress;

    public event Action? ShowWindowRequested;
    public event Action? ShowWindowUnlockedRequested;
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
                LaunchUnlockProcess();
                break;
            case UnlockMode.Pin:
                await TryUnlockWith(UnlockMode.Pin);
                break;
            case UnlockMode.WindowsHello:
                await TryUnlockWith(UnlockMode.WindowsHello);
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

    public async Task<bool> TryUnlockAsync(bool isAdmin)
    {
        if (!_isLocked)
            return true;
        if (Interlocked.CompareExchange(ref _unlockInProgress, 1, 0) != 0)
            return false;
        try
        {
            return await TryUnlockCoreAsync(isAdmin);
        }
        finally
        {
            Interlocked.Exchange(ref _unlockInProgress, 0);
        }
    }

    private async Task<bool> TryUnlockCoreAsync(bool isAdmin)
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
                await TryUnlockWith(UnlockMode.Pin);
                break;
            case UnlockMode.WindowsHello:
                await TryUnlockWith(UnlockMode.WindowsHello);
                break;
            case UnlockMode.Admin when !isAdmin:
                LaunchUnlockProcess();
                _unlockPolling = true;
                try
                {
                    var sw = Stopwatch.StartNew();
                    while (_isLocked && sw.ElapsedMilliseconds < 30_000)
                    {
                        await Task.Delay(100);
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
            autoLockTimerService.Stop();
            LockWindow();
            return;
        }

        autoLockTimerService.Start(timeoutSeconds, LockWindow);
    }

    public void StopAutoLockTimer() => autoLockTimerService.Stop();

    private async Task TryUnlockWith(UnlockMode mode)
    {
        _unlockPolling = true;
        try
        {
            if (mode == UnlockMode.Pin)
                PromptPinForUnlock();
            else
                await PromptWindowsHelloForUnlockAsync();
        }
        finally
        {
            _unlockPolling = false;
        }
    }

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

    private async Task PromptWindowsHelloForUnlockAsync()
    {
        var result = await windowsHello.VerifyAsync("Verify your identity to unlock RunFence");

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
        var store = session.CredentialStore;

        secureDesktop.Run(() =>
        {
            using var dlg = new PinDialog(PinDialogMode.Verify, allowReset: false);
            dlg.VerifyCallback = pin => pinService.VerifyPin(pin, store, out _);
            if (dlg.ShowDialog() == DialogResult.OK)
                verified = true;
        });

        if (verified)
        {
            session.LastPinVerifiedAt = DateTime.UtcNow;
            Unlock();
        }
    }

}