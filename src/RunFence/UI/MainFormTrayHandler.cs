using RunFence.Core;
using RunFence.Core.Models;
using RunFence.DragBridge;
using RunFence.Infrastructure;
using InfraWindowNative = RunFence.Infrastructure.WindowNative;
using RunFence.Launch;
using RunFence.Licensing;
using RunFence.Startup.UI;
using RunFence.TrayIcon;

namespace RunFence.UI;

/// <summary>
/// Manages tray icon, title/tooltip, hotkey registration, idle monitoring, discovery scheduling,
/// and tray launch wiring. Show-window/unlock request handling is delegated to
/// <see cref="MainFormWindowRequestHandler"/>; background auto-lock coordination is delegated
/// to <see cref="MainFormBackgroundAutoLockCoordinator"/>. Communicates with the form via
/// <see cref="IMainFormVisibility"/>.
/// </summary>
public class MainFormTrayHandler(
    LockManager lockManager,
    TrayLaunchHandler trayLaunchHandler,
    NotifyIcon notifyIcon,
    TrayIconManager trayIconManager,
    IIdleMonitorService idleMonitor,
    DiscoveryRefreshManager discoveryRefreshManager,
    SessionContext session,
    ILicenseService licenseService,
    IGlobalHotkeyService hotkeyService,
    MainFormWindowRequestHandler windowRequestHandler,
    MainFormBackgroundAutoLockCoordinator autoLockCoordinator)
    : IDisposable, ITrayBalloonService
{
    private const int AltEscapeHotkeyId = 0xAE01;
    private const int MOD_ALT = 0x0001;
    private const int VK_ESCAPE = 0x1B;

    private IMainFormVisibility _form = null!;
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

        windowRequestHandler.Initialize(form);
        windowRequestHandler.TitleUpdateNeeded += UpdateTitleAndTooltip;
        autoLockCoordinator.Initialize(form);

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
        UpdateTitleAndTooltip();
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

    public void UpdateTitleAndTooltip()
    {
        var title = licenseService.IsLicensed ? "RunFence" : "RunFence (Evaluation)";
        if (DebugHelper.UseAdminOperationMocks)
            title += " [NON-ELEVATED]";
        if (!string.IsNullOrEmpty(DebugHelper.AppId))
            title += $" [{DebugHelper.AppId}]";
        _form.Title = title;
        notifyIcon.Text = title;
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

    public void RestoreIconVisibility() => trayIconManager.RestoreIconVisibility();

    public void ShowBalloonTip(string text) => trayIconManager.ShowBalloonTip(text);

    public void ShowWarning(string text)
    {
        if (_form == null! || _form.IsDisposed || !_form.IsHandleCreated)
            return;
        _form.BeginInvokeOnUiThread(() => trayIconManager.ShowBalloonTip(text));
    }

    private void OnAltEscapeHotkey(int id)
    {
        if (id != AltEscapeHotkeyId)
            return;
        if (InfraWindowNative.GetForegroundWindow() != _form.Handle)
            return;
        // Defer to avoid modifying the hotkey list during the LL hook's foreach iteration.
        _form.BeginInvokeOnUiThread(autoLockCoordinator.HideToTray);
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
            autoLockCoordinator.HandleLicenseChanged();
            LicenseChangedRefreshNeeded?.Invoke(); // tells MainForm to call optionsPanel.SetData
        });
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        windowRequestHandler.TitleUpdateNeeded -= UpdateTitleAndTooltip;
        windowRequestHandler.CancelStartup();
        licenseService.LicenseStatusChanged -= OnLicenseStatusChanged;
        hotkeyService.HotkeyPressed -= OnAltEscapeHotkey;
        hotkeyService.Unregister(AltEscapeHotkeyId);
        trayIconManager.Dispose();
        notifyIcon.Visible = false;
        notifyIcon.Dispose();
        discoveryRefreshManager.Dispose();
    }
}
