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
        _form.Hide();
        if (licenseService.IsLicensed)
            lockManager.StartAutoLockTimer(immediateOnZero: true);
    }

    public void HandleAppDeactivated()
    {
        if (!windowRequestHandler.IsStartupComplete || lockManager.IsUnlocking || windowRequestHandler.IsShowingWindow || lockManager.IsLocked)
            return;
        if (_form.IsModalActive || _form.HasOtherWindowsOpen || !licenseService.IsLicensed || !_form.Visible)
            return;
        _backgroundTimerActive = true;
        lockManager.StartAutoLockTimer(immediateOnZero: false, postLockAction: HideToTrayFromLock);
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
            lockManager.StartAutoLockTimer(immediateOnZero: false, postLockAction: HideToTrayFromLock);
    }

    private void HideToTrayFromLock()
    {
        _backgroundTimerActive = false;
        _form.Hide();
    }
}
