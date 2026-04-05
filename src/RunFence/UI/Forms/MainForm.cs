using System.ComponentModel;
using Microsoft.Win32;
using RunFence.Account.UI.Forms;
using RunFence.Apps.UI;
using RunFence.Apps.UI.Forms;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Groups.UI.Forms;
using RunFence.Infrastructure;
using RunFence.Licensing;
using RunFence.Licensing.UI.Forms;
using RunFence.Persistence.UI;
using RunFence.TrayIcon;
using RunFence.Wizard.UI;

namespace RunFence.UI.Forms;

public partial class MainForm : Form, IMessageFilter, ITrayOwner, IMainFormVisibility
{
    private static readonly int WmTaskbarCreated = (int)NativeMethods.RegisterWindowMessage("TaskbarCreated");
    private readonly ApplicationsPanel _appsPanel;
    private readonly AccountsPanel _accountsPanel;
    private readonly GroupsPanel _groupsPanel;
    private readonly OptionsPanel _optionsPanel;

    private readonly SessionContext _session;
    private readonly ConfigManagementOrchestrator _configHandler;

    private readonly MainFormStartupOrchestrator _startupHandler;
    private readonly MainFormTrayHandler _trayHandler;
    private readonly ApplicationState _applicationState;

    private bool _suppressInitialVisibility;
    private bool _wasStartedInBackground;

    public event Action<ProtectedBuffer, ProtectedBuffer>? PinDerivedKeyReplaced;

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
        WizardLauncher wizardLauncher,
        ILicenseService licenseService)
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

        _trayHandler.PinResetCompleted += OnPinResetCompleted;
        _trayHandler.LicenseChangedRefreshNeeded += () => _optionsPanel.SetData(_session);

        Load += OnFormLoad;

        InitializeComponent();
        Icon = AppIcons.GetAppIcon();

        _appsPanel.DataChanged += HandleDataChanged;
        _appsPanel.EnforcementRequested += OnEnforcementRequested;
        _appsPanel.AccountNavigationRequested += OnNavigateToAccount;
        _appsPanel.WizardRequested += owner => wizardLauncher.OpenWizard(owner);
        _appsPanel.WizardButtonEnabled = true;
        wizardLauncher.WizardCompleted += HandleDataChanged;
        _applicationsTab.Controls.Add(_appsPanel);

        _accountsPanel.DataChanged += HandleDataChanged;
        _accountsPanel.AppNavigationRequested += OnNavigateToApp;
        _accountsPanel.NewAppRequested += OnNewAppForAccount;
        _accountsTab.Controls.Add(_accountsPanel);

        _groupsPanel.GroupsChanged += OnGroupsChanged;
        _groupsTab.Controls.Add(_groupsPanel);

        _optionsPanel.SettingsChanged += OnOptionsSettingsChanged;
        _optionsPanel.PinDerivedKeyChanged += OnPinDerivedKeyChanged;
        _optionsPanel.DataChanged += HandleDataChanged;
        _optionsPanel.CleanupRequested += OnCleanupRequested;
        _optionsPanel.ConfigLoadRequested += path =>
        {
            var (success, errorMessage) = _configHandler.LoadApps(path);
            if (!success && errorMessage != null)
                MessageBox.Show(errorMessage, "Load Config Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        };
        _optionsPanel.ConfigUnloadRequested += path => _configHandler.UnloadApps(path);
        _optionsTab.Controls.Add(_optionsPanel);

        _aboutTab.Controls.Add(new AboutPanel(licenseService));

        _tabControl.SelectedIndexChanged += (_, _) => ScheduleAvailabilityCheck();

        _trayHandler.Initialize(this, this);
        _trayHandler.UpdateTitleAndTooltip();

        Activated += (_, _) =>
        {
            ScheduleAvailabilityCheck();
            RefreshActivePanel();
        };
        Resize += OnResize;

        // Re-detect the interactive desktop user when a fast user switch occurs so that
        // the cached interactive user SID stays up to date for the new active session.
        SystemEvents.SessionSwitch += OnSessionSwitch;

        SetData();

        if (_session.Database.Apps.Count == 0)
            _tabControl.SelectedIndex = 1;
    }

    // IMainFormVisibility explicit implementations
    string IMainFormVisibility.Title
    {
        set => Text = value;
    }

    void IMainFormVisibility.BeginInvokeOnUiThread(Action action) => BeginInvoke(action);
    void IMainFormVisibility.InvokeOnUiThread(Action action) => Invoke(action);

    public IConfigManagementContext ConfigManagement => _configHandler;

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
        _trayHandler.SetStartupComplete();
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

    public void TryShowWindow() => _trayHandler.TryShowWindow();

    public void ConfigureIdleMonitor() => _trayHandler.ConfigureIdleMonitor();

    public void ShowWindowNormal() => _trayHandler.ShowAndActivate();
    public void ShowWindowUnlocked() => _trayHandler.ShowAndActivateForUnlock();

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

    protected override async void OnShown(EventArgs e)
    {
        if (!_wasStartedInBackground)
            PromptExportSettingsIfNeeded();
        base.OnShown(e);
        NativeInterop.ForceToForeground(this);
        _trayHandler.RefreshDiscovery();
        await _startupHandler.RunStartupChecksAsync(this, _wasStartedInBackground, _trayHandler.ShowNagIfNeeded);
    }

    private void PromptExportSettingsIfNeeded()
    {
        if (!string.IsNullOrEmpty(_session.Database.Settings.DefaultDesktopSettingsPath) && File.Exists(_session.Database.Settings.DefaultDesktopSettingsPath))
            return;
        var result = MessageBox.Show(this,
            "No desktop settings export file exists. Export your current desktop settings now?",
            "Export Desktop Settings",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);
        if (result != DialogResult.Yes)
            return;
        _tabControl.SelectedTab = _optionsTab;
        _optionsPanel.PerformExportSettings();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        if (e.Cancel)
            return;
        _applicationState.IsShuttingDown = true;
        foreach (var path in _configHandler.GetLoadedConfigPaths().ToList())
            _configHandler.UnloadApps(path);
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        Application.RemoveMessageFilter(this);
        _trayHandler.HandleFormClosing();
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        SidResolutionHelper.ReinitializeInteractiveUserSid();
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmTaskbarCreated)
            _trayHandler.RestoreIconVisibility();
        base.WndProc(ref m);
    }

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
        _tabControl.SelectedIndex = 1;
        _accountsPanel.SelectBySid(accountSid);
    }

    private void OnNavigateToApp(string appId)
    {
        _appsPanel.EditAppById(appId, new AppEditDialogOptions(LaunchNow: true));
    }

    private void OnNewAppForAccount(string accountSid)
    {
        _tabControl.SelectedIndex = 0;
        _appsPanel.OpenAddDialogForAccount(accountSid);
    }

    // --- Event handlers ---

    private void OnPinDerivedKeyChanged(ProtectedBuffer oldBuffer, CredentialStore newStore, ProtectedBuffer newBuffer)
    {
        SetData();
        PinDerivedKeyReplaced?.Invoke(oldBuffer, newBuffer);
    }

    private void OnPinResetCompleted(ProtectedBuffer oldBuffer, ProtectedBuffer newBuffer)
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
        _trayHandler.HandleResize();
    }

    private void RefreshActivePanel()
    {
        if (_trayHandler.LockManager.IsLocked)
            return;
        DataPanel? panel = _tabControl.SelectedIndex switch
        {
            1 => _accountsPanel,
            2 => _groupsPanel,
            _ => null
        };
        panel?.RefreshOnActivation();
    }

    private void OnCleanupRequested()
    {
        _configHandler.CleanupAllApps(_applicationState.EnforcementGuard.IsInProgress, _accountsPanel.IsOperationInProgress);
    }

    private void OnEnforcementRequested()
    {
        _startupHandler.RunEnforcement(this, _tabControl);
    }

    // --- Availability checks ---

    private void ScheduleAvailabilityCheck()
        => _configHandler.ScheduleAvailabilityCheck();

    // --- Event handler methods for AppLifecycleStarter wiring ---
    public void UpdateTray() => _trayHandler.UpdateTray();
    public Control GuardOwner => _tabControl;
}