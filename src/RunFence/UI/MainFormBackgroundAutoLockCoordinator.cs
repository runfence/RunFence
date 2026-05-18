using RunFence.Licensing;
using RunFence.Startup.UI;

namespace RunFence.UI;

/// <summary>
/// Coordinates background auto-lock and hide-to-tray behavior driven by app activation
/// and deactivation events from the main form.
/// </summary>
public class MainFormBackgroundAutoLockCoordinator(
    LockManager lockManager,
    MainFormWindowRequestHandler windowRequestHandler,
    ILicenseService licenseService)
{
    private IMainFormVisibility _form = null!;
    private bool _backgroundTimerActive;
    private const int VisibleAutoLockGracePeriodSeconds = 10;

    public void Initialize(IMainFormVisibility form)
    {
        _form = form;
    }

    public void HandleResize()
    {
        if (lockManager.IsUnlocking || windowRequestHandler.IsShowingWindow)
            return;

        if (_form.WindowState == FormWindowState.Minimized)
            HideToTray();
        else
        {
            _backgroundTimerActive = false;
            lockManager.StopAutoLockTimer();
        }
    }

    public void HideToTray()
    {
        if (lockManager.IsUnlocking || windowRequestHandler.IsShowingWindow)
            return;
        _backgroundTimerActive = false;
        if (!_form.Visible)
        {
            if (licenseService.IsLicensed)
                lockManager.StartAutoLockTimer(immediateOnZero: true);
            return;
        }

        _form.Hide();
        if (licenseService.IsLicensed)
            lockManager.StartAutoLockTimer(immediateOnZero: true);
    }

    public void LockToTrayImmediately()
    {
        if (lockManager.IsUnlocking || windowRequestHandler.IsShowingWindow)
            return;

        _backgroundTimerActive = false;
        lockManager.StopAutoLockTimer();
        if (_form.Visible)
            _form.Hide();
        if (!lockManager.IsLocked)
            lockManager.LockWindow();
    }

    public void HandleAppDeactivated()
    {
        if (!windowRequestHandler.IsStartupComplete || lockManager.IsUnlocking || windowRequestHandler.IsShowingWindow || lockManager.IsLocked)
            return;
        if (_form.IsModalActive || _form.HasOtherWindowsOpen || !licenseService.IsLicensed || !_form.Visible)
            return;
        _backgroundTimerActive = true;
        lockManager.StartAutoLockTimer(
            immediateOnZero: true,
            onTimeout: HandleVisibleWindowAutoLockTimeout);
    }

    public void HandleAppActivated()
    {
        if (!windowRequestHandler.IsStartupComplete)
            return;
        _backgroundTimerActive = false;
        lockManager.StopAutoLockTimer();
    }

    public void HandleWindowlessUnlock()
    {
        if (!windowRequestHandler.IsStartupComplete || lockManager.IsLocked)
            return;

        if (!_form.Visible && licenseService.IsLicensed)
            lockManager.StartAutoLockTimer(immediateOnZero: true);
    }

    /// <summary>
    /// Called from the license-status-changed handler to update auto-lock state after a license change.
    /// </summary>
    public void HandleLicenseChanged()
    {
        if (!licenseService.IsLicensed)
            lockManager.StopAutoLockTimer();
        else if (!_form.Visible)
            lockManager.StartAutoLockTimer(immediateOnZero: true);
        else if (_backgroundTimerActive)
            lockManager.StartAutoLockTimer(
                immediateOnZero: true,
                onTimeout: HandleVisibleWindowAutoLockTimeout);
    }

    private void HideToTrayFromLock()
    {
        _backgroundTimerActive = false;
        _form.Hide();
    }

    private void HandleVisibleWindowAutoLockTimeout()
    {
        if (!_backgroundTimerActive || lockManager.IsLocked)
            return;

        if (_form.Visible)
        {
            _form.Hide();
            lockManager.StartAutoLockTimer(
                immediateOnZero: false,
                onTimeout: HandleVisibleWindowAutoLockTimeout,
                timeoutOverrideSeconds: VisibleAutoLockGracePeriodSeconds);
            return;
        }

        _backgroundTimerActive = false;
        lockManager.LockWindow();
        HideToTrayFromLock();
    }
}
