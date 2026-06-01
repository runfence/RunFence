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

public partial class MainForm : ContextHelpForm, IMessageFilter, ITrayOwner, IMainFormVisibility, IMainFormContentView,
    IMainFormDataRefreshTarget, IMainFormLockTarget, IStartupFormLifetime, IStartupIpcHost, IDeferredStartupMainForm
{
    private static readonly int WmTaskbarCreated = (int)WindowNative.RegisterWindowMessage("TaskbarCreated");
    private const int WM_ACTIVATEAPP = 0x001C;
    internal static int TaskbarCreatedMessage => WmTaskbarCreated;
    internal static int ActivateAppMessage => WM_ACTIVATEAPP;
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
    private readonly MainFormContentCoordinator _contentCoordinator;
    private readonly MainFormMessageRouter _messageRouter;
    private event EventHandler? ActivationRefreshRequested;

    private bool _suppressInitialVisibility;
    private bool _wasStartedInBackground;

    // Reviewed: 14 deps are justified — each is an already-extracted independent handler with
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
        MainFormContentCoordinator contentCoordinator,
        MainFormMessageRouter messageRouter,
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
        _contentCoordinator = contentCoordinator;
        _messageRouter = messageRouter;
        _contentCoordinator.Initialize(this);

        _trayHandler.LicenseChangedRefreshNeeded += () => _optionsPanel.SetData(_session);

        Load += OnFormLoad;

        InitializeComponent();
        Icon = AppIcons.GetAppIcon();
        _tabControl.SelectedIndexChanged += OnActivationRefreshRequested;
        Activated += OnActivationRefreshRequested;
        Resize += OnResize;

        _contentCoordinator.BuildTabs(aboutPanel);
        SetData();
    }

    private void HandleConfigLoadResult(string path, LoadAppsResult result, bool allowBackupRestore = true)
    {
        if (!result.Succeeded && result.ErrorMessage != null)
        {
            if (allowBackupRestore && result.BackupAvailable)
            {
                if (!ConfirmRestoreAppConfigBackup(result.ErrorMessage))
                    return;

                HandleConfigLoadResult(path, _configHandler.LoadAppConfigBackup(path), allowBackupRestore: false);
                return;
            }

            MessageBox.Show(result.ErrorMessage, "Load Config Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        else if (result.Warnings is { Count: > 0 })
        {
            MessageBox.Show(string.Join("\n\n", result.Warnings), "Load Config Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private bool ConfirmRestoreAppConfigBackup(string errorMessage)
    {
        var useBackupButton = new TaskDialogButton("Use Backup");
        var cancelButton = new TaskDialogButton("Cancel");
        var page = new TaskDialogPage
        {
            Caption = "Load Config Failed",
            Heading = "This app config cannot be read.",
            Text = $"{errorMessage}\n\n\"Use Backup\" will restore the last version of this config that loaded successfully.",
            Icon = TaskDialogIcon.Error,
            Buttons = { useBackupButton, cancelButton },
            DefaultButton = useBackupButton,
            AllowCancel = true
        };

        return TaskDialog.ShowDialog(this, page) == useBackupButton;
    }

    // IMainFormVisibility explicit implementations
    string IMainFormVisibility.Title
    {
        set => Text = value;
    }

    void IMainFormVisibility.BeginInvokeOnUiThread(Action action) => BeginInvoke(action);
    void IMainFormVisibility.InvokeOnUiThread(Action action) => Invoke(action);
    void IStartupIpcHost.BeginInvokeOnUiThread(Action action) => BeginInvoke(action);
    bool IDeferredStartupMainForm.IsDisposed => IsDisposed;
    void IDeferredStartupMainForm.BeginInvokeOnUiThread(Action action) => BeginInvoke(action);

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
    public void LockToTrayImmediately() => _trayHandler.LockToTrayImmediately();
    public bool IsLocked => _trayHandler.LockManager.IsLocked;

    public void ShowWindowNormal() => _windowRequestHandler.ShowAndActivate();
    public void ShowWindowUnlocked() => _windowRequestHandler.ShowAndActivateForUnlock();
    public void HandleWindowlessUnlock() => _autoLockCoordinator.HandleWindowlessUnlock();
    public bool IsTrayLockVisible => _trayHandler.IsTrayLockVisible;
    public bool IsTrayLockEnabled => _trayHandler.IsTrayLockEnabled;

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
        QueueSelectedTabRefresh();
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
    }

    protected override void WndProc(ref Message m)
    {
        _messageRouter.HandleWndProc(m);
        base.WndProc(ref m);
    }

    bool IMainFormVisibility.IsModalActive => _applicationState.IsModalOpen;
    bool IMainFormVisibility.HasOtherWindowsOpen => Application.OpenForms.OfType<Form>().Any(f => f != this && f.Visible);

    public bool PreFilterMessage(ref Message m) => _messageRouter.PreFilterMessage(ref m);

    // --- Data flow ---

    public void SetData()
    {
        _contentCoordinator.SetData(_session.Database);
    }

    public void HandleDataChanged()
    {
        _contentCoordinator.HandleDataChanged();
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

        (_tabControl.SelectedTab switch
        {
            var t when t == _applicationsTab => (DataPanel?)_appsPanel,
            var t when t == _accountsTab => _accountsPanel,
            var t when t == _groupsTab => _groupsPanel,
            var t when t == _optionsTab => _optionsPanel,
            _ => null,
        })?.RefreshOnActivation();
    }

    private void QueueSelectedTabRefresh()
    {
        if (!IsHandleCreated || IsDisposed)
            return;

        BeginInvoke(() =>
        {
            if (IsDisposed)
                return;

            _tabControl.SelectedTab?.PerformLayout();
            RefreshActivePanel();
        });
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

    event EventHandler IMainFormContentView.ActivationRefreshRequested
    {
        add => ActivationRefreshRequested += value;
        remove => ActivationRefreshRequested -= value;
    }

    Control IMainFormContentView.FormControl => this;

    void IMainFormContentView.AttachApplicationsPanel(ApplicationsPanel panel)
    {
        _applicationsTab.Controls.Add(panel);
        panel.Dock = DockStyle.Fill;
    }

    void IMainFormContentView.AttachAccountsPanel(AccountsPanel panel)
    {
        _accountsTab.Controls.Add(panel);
        panel.Dock = DockStyle.Fill;
        panel.RegisterContextHelp(this);
    }

    void IMainFormContentView.AttachGroupsPanel(GroupsPanel panel)
    {
        _groupsTab.Controls.Add(panel);
        panel.Dock = DockStyle.Fill;
    }

    void IMainFormContentView.AttachOptionsPanel(OptionsPanel panel)
    {
        _optionsTab.Controls.Add(panel);
        panel.Dock = DockStyle.Fill;
        panel.RegisterContextHelp(this);
    }

    void IMainFormContentView.AttachAboutPanel(AboutPanel panel)
    {
        _aboutTab.Controls.Add(panel);
        panel.Dock = DockStyle.Fill;
    }

    void IMainFormContentView.SelectAccountsTab() => _tabControl.SelectedTab = _accountsTab;
    void IMainFormContentView.ScheduleAvailabilityCheck() => ScheduleAvailabilityCheck();
    void IMainFormContentView.QueueSelectedTabRefresh() => QueueSelectedTabRefresh();
    void IMainFormContentView.NavigateToAccount(string accountSid) => OnNavigateToAccount(accountSid);
    void IMainFormContentView.NavigateToApp(string appId) => OnNavigateToApp(appId);
    void IMainFormContentView.OpenAddDialogForAccount(string accountSid) => OnNewAppForAccount(accountSid);
    void IMainFormContentView.HandleOptionsSettingsChanged() => OnOptionsSettingsChanged();
    void IMainFormContentView.RequestCleanup() => OnCleanupRequested();
    void IMainFormContentView.RequestMigrationExit() => Application.Exit();
    void IMainFormContentView.RequestConfigLoad(string path)
    {
        var result = _configHandler.LoadApps(path);
        HandleConfigLoadResult(path, result);
    }
    void IMainFormContentView.RequestConfigUnload(string path) => _configHandler.UnloadApps(path);
    void IMainFormContentView.RequestEnforcement() => OnEnforcementRequested();

    private void OnActivationRefreshRequested(object? sender, EventArgs e)
    {
        ActivationRefreshRequested?.Invoke(this, EventArgs.Empty);
    }
}
