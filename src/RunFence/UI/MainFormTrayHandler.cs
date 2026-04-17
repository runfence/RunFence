using RunFence.Core;
using RunFence.Core.Models;
using RunFence.DragBridge;
using RunFence.Infrastructure;
using InfraWindowNative = RunFence.Infrastructure.WindowNative;
using RunFence.Launch;
using RunFence.Licensing;
using RunFence.Licensing.UI.Forms;
using RunFence.Persistence.UI;
using RunFence.Startup.UI;
using RunFence.TrayIcon;

namespace RunFence.UI;

/// <summary>
/// Manages tray icon, show/hide, auto-lock timer, Alt+Escape hotkey, idle monitoring, and
/// discovery scheduling. Extracted from <see cref="RunFence.UI.Forms.MainForm"/> to reduce its
/// dependency count. Communicates with the form via <see cref="IMainFormVisibility"/>.
/// </summary>
/// <remarks>
/// Dependency count (11): at threshold but no actionable split exists. Each dep covers a
/// distinct concern: lock manager, tray launch, notify icon, tray icon manager, idle monitor,
/// discovery refresh, session, config orchestrator, license, launch facade, hotkey service —
/// all interacting with the tray lifecycle. Reviewed 2026-04-16.
/// </remarks>
public class MainFormTrayHandler(
    LockManager lockManager,
    TrayLaunchHandler trayLaunchHandler,
    NotifyIcon notifyIcon,
    TrayIconManager trayIconManager,
    IIdleMonitorService idleMonitor,
    DiscoveryRefreshManager discoveryRefreshManager,
    SessionContext session,
    ConfigManagementOrchestrator configManagementOrchestrator,
    ILicenseService licenseService,
    ILaunchFacade launchFacade,
    IGlobalHotkeyService hotkeyService)
    : IDisposable
{
    private const int AltEscapeHotkeyId = 0xAE01;
    private const int MOD_ALT = 0x0001;
    private const int VK_ESCAPE = 0x1B;

    private IMainFormVisibility _form = null!;
    private bool _startupComplete;
    private bool _disposed;

    /// <summary>
    /// Raised when the license status changes and the options panel should be refreshed.
    /// </summary>
    public event Action? LicenseChangedRefreshNeeded;

    public LockManager LockManager { get; } = lockManager;

    /// <summary>
    /// Call once from MainForm constructor after InitializeComponent, passing the form itself.
    /// Sets up tray icon, discovery manager, lock manager, idle monitor, and license events.
    /// The form must implement both <see cref="IMainFormVisibility"/> and <see cref="ITrayOwner"/>.
    /// </summary>
    public void Initialize(IMainFormVisibility form, Control formAsControl)
    {
        _form = form;

        trayIconManager.Initialize((ITrayOwner)form);
        trayIconManager.AppLaunchRequested += trayLaunchHandler.LaunchApp;
        trayIconManager.FolderBrowserLaunchRequested += (sid, shift) =>
            trayLaunchHandler.LaunchFolderBrowser(new AccountLaunchIdentity(sid)
                { PrivilegeLevel = shift ? PrivilegeLevel.HighestAllowed : null });
        trayIconManager.TerminalLaunchRequested += (sid, shift) =>
            trayLaunchHandler.LaunchTerminal(new AccountLaunchIdentity(sid)
                { PrivilegeLevel = shift ? PrivilegeLevel.HighestAllowed : null });
        trayIconManager.DiscoveredAppLaunchRequested += (exe, sid) =>
            trayLaunchHandler.LaunchDiscoveredApp(exe, new AccountLaunchIdentity(sid));
        trayIconManager.UpdateDatabase(session.CredentialStore);
        discoveryRefreshManager.SetHost(trayIconManager, formAsControl);

        idleMonitor.IdleTimeoutReached += () =>
        {
            if (!_form.IsDisposed && licenseService.IsLicensed)
                Application.Exit();
        };

        licenseService.LicenseStatusChanged += OnLicenseStatusChanged;
    }

    public void HandleFormLoad()
    {
        hotkeyService.HotkeyPressed += OnAltEscapeHotkey;
        hotkeyService.Register(AltEscapeHotkeyId, MOD_ALT, VK_ESCAPE, consume: false);
    }

    public void HandleFormClosing()
    {
        Dispose();
    }

    public void SetStartupComplete() => _startupComplete = true;

    public void HandleResize()
    {
        if (LockManager.IsUnlocking || IsShowingWindow)
            return;

        if (_form.WindowState == FormWindowState.Minimized)
            HideToTray();
        else
            LockManager.StopAutoLockTimer();
    }

    public void HideToTray()
    {
        if (LockManager.IsUnlocking || IsShowingWindow)
            return;
        _form.Hide();
        if (licenseService.IsLicensed)
            LockManager.StartAutoLockTimer();
    }

    public void ShowAndActivate()
    {
        _form.ShowInTaskbar = true;
        IsShowingWindow = true;
        try
        {
            // Reset to Normal before Show so the form never appears in minimized state.
            _form.WindowState = FormWindowState.Normal;
            _form.Show();
        }
        finally
        {
            IsShowingWindow = false;
        }

        ForceForeground();
    }

    public void ShowAndActivateForUnlock()
    {
        // Show before setting WindowState — prevents brief minimized flash when unlocking
        _form.ShowInTaskbar = true;
        _form.Show();
        _form.WindowState = FormWindowState.Normal;
        ForceForeground();
    }

    public async Task TryShowWindowAsync()
    {
        if (!_startupComplete)
            return;
        var wasLocked = LockManager.IsLocked;
        await LockManager.TryShowWindowAsync();
        if (wasLocked && !LockManager.IsLocked)
            configManagementOrchestrator.ScheduleAvailabilityCheck();
        if (!LockManager.IsLocked)
        {
            ShowNagIfNeeded();
            ForceForeground();
        }
    }

    public void ShowNagIfNeeded()
    {
        if (!licenseService.ShouldShowNag(DateTime.Now))
            return;
        using var dlg = new EvaluationNagDialog(licenseService, launchFacade);
        dlg.ShowDialog((Control)_form);
        if (licenseService.IsLicensed)
            UpdateTitleAndTooltip();
        else
            licenseService.RecordNagShown(DateTime.Now);
    }

    public void UpdateTitleAndTooltip()
    {
        var isLicensed = licenseService.IsLicensed;
        _form.Title = isLicensed ? "RunFence" : "RunFence (Evaluation)";
        notifyIcon.Text = isLicensed ? "RunFence" : "RunFence (Evaluation)";
    }

    public void UpdateTray()
        => trayIconManager.UpdateDatabase(session.CredentialStore);

    public void ScheduleDiscoveryRefresh()
        => discoveryRefreshManager.Schedule();

    public void RefreshDiscovery()
        => discoveryRefreshManager.Refresh();

    public void ConfigureIdleMonitor()
    {
        idleMonitor.Configure(session.Database.Settings.IdleTimeoutMinutes);
        if (session.Database.Settings.IdleTimeoutMinutes > 0)
            idleMonitor.Start();
        else
            idleMonitor.Stop();
    }

    public void ResetIdleTimer() => idleMonitor.ResetIdleTimer();

    public bool IsShowingWindow { get; private set; }

    public void RestoreIconVisibility() => trayIconManager.RestoreIconVisibility();

    private void OnAltEscapeHotkey(int id)
    {
        if (id != AltEscapeHotkeyId)
            return;
        if (InfraWindowNative.GetForegroundWindow() != _form.Handle)
            return;
        // Defer to avoid modifying the hotkey list during the LL hook's foreach iteration.
        _form.BeginInvokeOnUiThread(HideToTray);
    }

    private void OnLicenseStatusChanged()
    {
        if (_form.IsDisposed || !_form.IsHandleCreated)
            return;
        _form.BeginInvokeOnUiThread(() =>
        {
            if (_form.IsDisposed)
                return;
            UpdateTitleAndTooltip();
            ConfigureIdleMonitor();
            if (!licenseService.IsLicensed)
                LockManager.StopAutoLockTimer();
            else if (_form.WindowState == FormWindowState.Minimized)
                LockManager.StartAutoLockTimer();
            LicenseChangedRefreshNeeded?.Invoke(); // tells MainForm to call optionsPanel.SetData
        });
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        licenseService.LicenseStatusChanged -= OnLicenseStatusChanged;
        hotkeyService.HotkeyPressed -= OnAltEscapeHotkey;
        hotkeyService.Unregister(AltEscapeHotkeyId);
        trayIconManager.Dispose();
        notifyIcon.Visible = false;
        notifyIcon.Dispose();
        discoveryRefreshManager.Dispose();
    }

    private void ForceForeground()
    {
        WindowForegroundHelper.ForceToForeground(_form.Handle);
        _form.BringToFront();
    }
}