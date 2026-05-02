using System.ComponentModel;
using RunFence.Account.UI.Forms;
using RunFence.Apps.UI;
using RunFence.Apps.UI.Forms;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Groups.UI.Forms;
using RunFence.Infrastructure;
using RunFence.Licensing.UI.Forms;
using RunFence.Persistence.UI;
using RunFence.TrayIcon;

namespace RunFence.UI.Forms;

public partial class MainForm : Form, IMessageFilter, ITrayOwner, IMainFormVisibility,
    IMainFormDataRefreshTarget, IMainFormLockTarget, IStartupFormLifetime
{
    private static readonly int WmTaskbarCreated = (int)WindowNative.RegisterWindowMessage("TaskbarCreated");
    private const int WM_ACTIVATEAPP = 0x001C;
    private readonly ApplicationsPanel _appsPanel;
    private readonly AccountsPanel _accountsPanel;
    private readonly GroupsPanel _groupsPanel;
    private readonly OptionsPanel _optionsPanel;

    private readonly SessionContext _session;
    private readonly ConfigManagementOrchestrator _configHandler;

    private readonly MainFormStartupOrchestrator _startupHandler;
    private readonly MainFormTrayHandler _trayHandler;
    private readonly MainFormWindowRequestHandler _windowRequestHandler;
    private readonly MainFormBackgroundAutoLockCoordinator _autoLockCoordinator;
    private readonly ApplicationState _applicationState;

    private bool _suppressInitialVisibility;
    private bool _wasStartedInBackground;

    public event Action<ProtectedBuffer, ProtectedBuffer>? PinDerivedKeyReplaced;

    // Reviewed: 12 deps are justified — each is an already-extracted independent handler with
    // no overlap in responsibility. MainForm is the composition root for the main window.
    public MainForm(
        SessionContext session,
        ApplicationState applicationState,
        ConfigManagementOrchestrator configHandler,
        ApplicationsPanel appsPanel,
        AccountsPanel accountsPanel,
        GroupsPanel groupsPanel,
        OptionsPanel optionsPanel,
        MainFormStartupOrchestrator startupHandler,
        MainFormTrayHandler trayHandler,
        MainFormWindowRequestHandler windowRequestHandler,
        MainFormBackgroundAutoLockCoordinator autoLockCoordinator,
        AboutPanel aboutPanel)
    {
        _session = session;
        _applicationState = applicationState;
        _configHandler = configHandler;
        _appsPanel = appsPanel;
        _accountsPanel = accountsPanel;
        _groupsPanel = groupsPanel;
        _optionsPanel = optionsPanel;
        _startupHandler = startupHandler;
        _trayHandler = trayHandler;
        _windowRequestHandler = windowRequestHandler;
        _autoLockCoordinator = autoLockCoordinator;

        _trayHandler.LicenseChangedRefreshNeeded += () => _optionsPanel.SetData(_session);

        Load += OnFormLoad;

        InitializeComponent();
        Icon = AppIcons.GetAppIcon();

        _appsPanel.DataChanged += HandleDataChanged;
        _appsPanel.EnforcementRequested += OnEnforcementRequested;
        _appsPanel.AccountNavigationRequested += OnNavigateToAccount;
        _applicationsTab.Controls.Add(_appsPanel);
        _appsPanel.Dock = DockStyle.Fill;

        _accountsPanel.DataChanged += HandleDataChanged;
        _accountsPanel.AppNavigationRequested += OnNavigateToApp;
        _accountsPanel.NewAppRequested += OnNewAppForAccount;
        _accountsTab.Controls.Add(_accountsPanel);
        _accountsPanel.Dock = DockStyle.Fill;

        _groupsPanel.GroupsChanged += OnGroupsChanged;
        _groupsTab.Controls.Add(_groupsPanel);
        _groupsPanel.Dock = DockStyle.Fill;

        _optionsPanel.SettingsChanged += OnOptionsSettingsChanged;
        _optionsPanel.PinDerivedKeyChanged += OnPinDerivedKeyChanged;
        _optionsPanel.DataChanged += HandleDataChanged;
        _optionsPanel.CleanupRequested += OnCleanupRequested;
        _optionsPanel.MigrationExitRequested += () => Application.Exit();
        _optionsPanel.ConfigLoadRequested += path =>
        {
            var (success, errorMessage) = _configHandler.LoadApps(path);
            if (!success && errorMessage != null)
                MessageBox.Show(errorMessage, "Load Config Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        };
        _optionsPanel.ConfigUnloadRequested += path => _configHandler.UnloadApps(path);
        _optionsTab.Controls.Add(_optionsPanel);
        _optionsPanel.Dock = DockStyle.Fill;

        _aboutTab.Controls.Add(aboutPanel);
        aboutPanel.Dock = DockStyle.Fill;

        _tabControl.SelectedIndexChanged += (_, _) => ScheduleAvailabilityCheck();

        _trayHandler.Initialize(this, this);
        _trayHandler.UpdateTitleAndTooltip();

        Activated += (_, _) =>
        {
            ScheduleAvailabilityCheck();
            RefreshActivePanel();
        };
        Resize += OnResize;

        SetData();

        if (_session.Database.Apps.Count == 0)
            _tabControl.SelectedTab = _accountsTab;
    }

    // IMainFormVisibility explicit implementations
    string IMainFormVisibility.Title
    {
        set => Text = value;
    }

    void IMainFormVisibility.BeginInvokeOnUiThread(Action action) => BeginInvoke(action);
    void IMainFormVisibility.InvokeOnUiThread(Action action) => Invoke(action);

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool SuppressInitialVisibility
    {
        set
        {
            _suppressInitialVisibility = value;
            _wasStartedInBackground = value;
        }
    }

    public void SetStartupComplete()
    {
        _windowRequestHandler.SetStartupComplete();
    }

    protected override void SetVisibleCore(bool value)
    {
        if (_suppressInitialVisibility)
        {
            _suppressInitialVisibility = false;
            if (!IsHandleCreated)
                CreateHandle();
            base.SetVisibleCore(false);
            return;
        }

        base.SetVisibleCore(value);
    }

    public Task TryShowWindowAsync() => _windowRequestHandler.TryShowWindowAsync();
    public Task<bool> HandleElevatedUnlockRequestAsync() => _windowRequestHandler.HandleElevatedUnlockRequestAsync();

    public void ConfigureIdleMonitor() => _trayHandler.ConfigureIdleMonitor();

    public void ShowWindowNormal() => _windowRequestHandler.ShowAndActivate();
    public void ShowWindowUnlocked() => _windowRequestHandler.ShowAndActivateForUnlock();
    public void HandleWindowlessUnlock() => _autoLockCoordinator.HandleWindowlessUnlock();

    public bool ConfirmWindowsHelloUnavailableFallback() =>
        MessageBox.Show(this,
            "Windows Hello is unavailable on the current account. Use PIN instead?",
            "Windows Hello Unavailable",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning) == DialogResult.Yes;

    public bool ConfirmWindowsHelloFailedFallback() =>
        MessageBox.Show(this,
            "Windows Hello verification failed. Use PIN instead?",
            "Windows Hello Failed",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning) == DialogResult.Yes;

    private void OnFormLoad(object? sender, EventArgs e)
    {
        Application.AddMessageFilter(this);
        _trayHandler.HandleFormLoad();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        WindowForegroundHelper.ForceToForeground(Handle);
        BringToFront();
        _trayHandler.RefreshDiscovery();
        BeginInvoke(async void () =>
        {
            try
            {
                await _startupHandler.RunStartupChecksAsync(this, _wasStartedInBackground, _windowRequestHandler.ShowNagIfNeeded);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Startup checks failed:\n{ex.Message}", "RunFence — Startup Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        });
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        if (e.Cancel)
            return;
        _applicationState.IsShuttingDown = true;
        foreach (var path in _configHandler.GetLoadedConfigPaths().ToList())
            _configHandler.UnloadApps(path);
        Application.RemoveMessageFilter(this);
        _trayHandler.HandleFormClosing();
        _configHandler.TerminateEmptyJobKeepers();
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmTaskbarCreated)
            _trayHandler.RestoreIconVisibility();
        if (m.Msg == WM_ACTIVATEAPP)
        {
            if (m.WParam == IntPtr.Zero)
                _autoLockCoordinator.HandleAppDeactivated();
            else
                _autoLockCoordinator.HandleAppActivated();
        }
        base.WndProc(ref m);
    }

    bool IMainFormVisibility.IsModalActive => _applicationState.IsModalOpen;
    bool IMainFormVisibility.HasOtherWindowsOpen => Application.OpenForms.OfType<Form>().Any(f => f != this && f.Visible);

    public bool PreFilterMessage(ref Message m)
    {
        const int WM_KEYDOWN = 0x0100;
        const int WM_MOUSEMOVE = 0x0200;
        const int WM_LBUTTONDOWN = 0x0201;
        const int WM_RBUTTONDOWN = 0x0204;
        const int WM_MOUSEWHEEL = 0x020A;

        var msg = m.Msg;
        if (msg is WM_KEYDOWN or WM_MOUSEMOVE or WM_LBUTTONDOWN or WM_RBUTTONDOWN or WM_MOUSEWHEEL)
        {
            _trayHandler.ResetIdleTimer();
        }

        return false;
    }

    // --- Data flow ---

    public void SetData()
    {
        _appsPanel.SetData(_session);
        _accountsPanel.SetData(_session);
        _groupsPanel.SetData(_session);
        _optionsPanel.SetData(_session);
    }

    public void HandleDataChanged()
    {
        _trayHandler.UpdateTray();
        _appsPanel.SetData(_session);
        _accountsPanel.SetData(_session);
        _groupsPanel.SetData(_session);
        _optionsPanel.SetData(_session);
        _trayHandler.ScheduleDiscoveryRefresh();
    }

    private void OnGroupsChanged()
    {
        _trayHandler.UpdateTray();
        _accountsPanel.SetData(_session);
        _groupsPanel.SetData(_session);
        _trayHandler.ScheduleDiscoveryRefresh();
    }

    // --- Navigation ---

    private void OnNavigateToAccount(string accountSid)
    {
        _tabControl.SelectedTab = _accountsTab;
        _accountsPanel.SelectBySid(accountSid);
    }

    private void OnNavigateToApp(string appId)
    {
        _appsPanel.EditAppById(appId, new AppEditDialogOptions(LaunchNow: true));
    }

    private void OnNewAppForAccount(string accountSid)
    {
        _tabControl.SelectedTab = _applicationsTab;
        _appsPanel.OpenAddDialogForAccount(accountSid);
    }

    // --- Event handlers ---

    private void OnPinDerivedKeyChanged(ProtectedBuffer oldBuffer, CredentialStore newStore, ProtectedBuffer newBuffer)
    {
        SetData();
        PinDerivedKeyReplaced?.Invoke(oldBuffer, newBuffer);
    }

    private void OnOptionsSettingsChanged()
    {
        _trayHandler.ConfigureIdleMonitor();
    }

    private void OnResize(object? sender, EventArgs e)
    {
        _autoLockCoordinator.HandleResize();
    }

    private void RefreshActivePanel()
    {
        if (_trayHandler.LockManager.IsLocked)
            return;
        DataPanel? panel = _tabControl.SelectedTab switch
        {
            var t when t == _accountsTab => _accountsPanel,
            var t when t == _groupsTab => _groupsPanel,
            _ => null
        };
        panel?.RefreshOnActivation();
    }

    private void OnCleanupRequested()
    {
        var result = _configHandler.CleanupAllApps(_applicationState.EnforcementGuard.IsInProgress, _accountsPanel.IsOperationInProgress);
        switch (result)
        {
            case CleanupAllAppsResult.OperationInProgress:
                MessageBox.Show(this,
                    "An operation is currently in progress. Please wait for it to finish before cleaning up.",
                    "Cleanup Unavailable",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                break;
            case CleanupAllAppsResult.ReadyToExit:
                Application.Exit();
                break;
        }
    }

    private void OnEnforcementRequested()
    {
        _startupHandler.RunEnforcement(this, _tabControl);
    }

    // --- Availability checks ---

    private void ScheduleAvailabilityCheck()
        => _configHandler.ScheduleAvailabilityCheck();

    public void ShowTrayBalloon(string text) => _trayHandler.ShowBalloonTip(text);

    // --- Event handler methods for AppLifecycleStarter wiring ---
    public void UpdateTray() => _trayHandler.UpdateTray();
    public Control GuardOwner => _tabControl;
}
