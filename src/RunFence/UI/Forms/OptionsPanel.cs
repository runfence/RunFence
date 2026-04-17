using RunFence.Apps;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.DragBridge.UI.Forms;
using RunFence.Firewall;
using RunFence.Infrastructure;
using RunFence.Launch;
using RunFence.Persistence.UI.Forms;
using RunFence.Security;
using RunFence.Startup.UI;
using RunFence.UI;

namespace RunFence.UI.Forms;

/// <remarks>Deps above threshold: All deps are already-extracted independent feature handlers or section objects (PIN change, security check, auto-start, folder browser section, checkbox handler, data loader, IPC, config manager, drag bridge, ICMP section, pending edit notifier). Splitting the form by tab would require cross-tab state sharing (e.g., settings save) that doesn't exist today, creating coupling where there is none. Reviewed 2026-04-17.</remarks>
public partial class OptionsPanel : DataPanel
{
    private Form? _parentForm;

    private readonly OptionsSettingsHandler _settingsHandler;
    private readonly IContextMenuService _contextMenuService;
    private readonly ILoggingService _log;
    private readonly ILaunchFacade _launchFacade;
    private readonly OperationGuard _operationGuard = new();
    private readonly PinChangeOrchestrator _pinChangeHandler;
    private readonly SecurityCheckRunner _securityCheckHandler;
    private readonly OptionsAutoFeatureHandler _autoFeatureHandler;
    private readonly OptionsPanelDataLoader _dataLoader;
    private readonly OptionsIcmpSection _icmpSection;
    private readonly OptionsPendingEditNotifier _pendingEditNotifier;
    private readonly OptionsFolderBrowserSection _folderBrowserSection;
    private readonly OptionsPanelCheckboxHandler _checkboxHandler;

    private readonly IpcCallerSection _callerSection;
    private readonly ConfigManagerSection _configSection;
    private readonly DragBridgeSection _dragBridgeSection;

    public event Action? SettingsChanged;
    public event Action? DataChanged;
    public event Action<ProtectedBuffer, CredentialStore, ProtectedBuffer>? PinDerivedKeyChanged;
    public event Action? CleanupRequested;
    public event Action<string>? ConfigLoadRequested;
    public event Action<string>? ConfigUnloadRequested;
    public event Action? DragBridgeSettingsChanged;

    public OptionsPanel(
        IModalCoordinator modalCoordinator,
        OptionsSettingsHandler settingsHandler,
        IContextMenuService contextMenuService,
        PinChangeOrchestrator pinChangeOrchestrator,
        SecurityCheckRunner securityCheckRunner,
        ILoggingService log,
        OptionsAutoFeatureHandler autoFeatureHandler,
        ILaunchFacade launchFacade,
        OptionsPanelDataLoader dataLoader,
        OptionsIcmpSection icmpSection,
        OptionsFolderBrowserSection folderBrowserSection,
        OptionsPanelCheckboxHandler checkboxHandler,
        Func<IpcCallerSection> ipcCallerSectionFactory,
        Func<ConfigManagerSection> configManagerSectionFactory,
        Func<DragBridgeSection> dragBridgeSectionFactory)
        : base(modalCoordinator)
    {
        _settingsHandler = settingsHandler;
        _contextMenuService = contextMenuService;
        _log = log;
        _launchFacade = launchFacade;
        _pinChangeHandler = pinChangeOrchestrator;
        _securityCheckHandler = securityCheckRunner;
        _autoFeatureHandler = autoFeatureHandler;
        _dataLoader = dataLoader;
        _icmpSection = icmpSection;
        _folderBrowserSection = folderBrowserSection;
        _checkboxHandler = checkboxHandler;

        _callerSection = ipcCallerSectionFactory();
        _callerSection.SetShowModalDialog(dlg => ShowModalDialog(dlg));
        _callerSection.Changed += OnCallerChanged;

        _configSection = configManagerSectionFactory();
        _configSection.ConfigLoadRequested += path => ConfigLoadRequested?.Invoke(path);
        _configSection.ConfigUnloadRequested += path => ConfigUnloadRequested?.Invoke(path);
        _configSection.DataChanged += () => DataChanged?.Invoke();

        _dragBridgeSection = dragBridgeSectionFactory();
        _dragBridgeSection.Changed += OnDragBridgeSettingsChanged;

        _pendingEditNotifier = new OptionsPendingEditNotifier(SaveSettings);

        InitializeComponent();

        _openLogButton.Left = _enableLoggingCheckBox.Right + 8;

        // Set images (runtime bitmap generation)
        _changePinBtn.Image = UiIconFactory.CreateToolbarIcon("\u2731", Color.FromArgb(0x33, 0x66, 0x99), 16);
        _cleanupBtn.Image = UiIconFactory.CreateToolbarIcon("\u2716", Color.FromArgb(0x99, 0x33, 0x33), 16);
        _securityCheckBtn.Image = UiIconFactory.CreateToolbarIcon("\u26A0", Color.FromArgb(0x22, 0x8B, 0x22), 16);

        // Tooltips
        _tooltip.SetToolTip(_cleanupBtn, "Reverts all ACLs and shortcuts for all managed apps, then exits");
        _tooltip.SetToolTip(_securityCheckBtn, "Scan startup folders and registry for security issues");
        _tooltip.SetToolTip(_autoLockTimeoutUpDown, "0 = lock immediately when minimized");
        _callerSection.SetGroupTitle("Launcher Access Control");
        _callerSection.SetDescription("Restrict which accounts can launch apps via IPC. Empty = unrestricted.");
        _tooltip.SetToolTip(_exportSettingsButton, "Export current session's desktop settings to a file");

        // Add section UserControls to their containers
        _dragBridgeSection.Dock = DockStyle.Fill;
        _dragBridgePlaceholder.Controls.Add(_dragBridgeSection);

        _callerSection.Dock = DockStyle.Fill;
        _callerGroup.Controls.Add(_callerSection);

        _configSection.Dock = DockStyle.Fill;
        _rightConfigPanel.Controls.Add(_configSection);

        // Wire runtime controls into injected sections after InitializeComponent().
        // Note: folder browser exe-path Leave is wired inside OptionsFolderBrowserSection.Initialize.
        _folderBrowserSection.Initialize(
            _folderBrowserExeTextBox,
            _defaultSettingsPathTextBox,
            _exportSettingsButton,
            _operationGuard,
            this,
            () => Database.Settings,
            DebounceSave);

        _checkboxHandler.Initialize(
            _autoLockCheckBox,
            _autoLockTimeoutUpDown,
            () => Database.Settings,
            SaveSettings);

        _icmpSection.Initialize(
            _blockIcmpCheckBox,
            SaveSettings);

        // TextChanged handlers (OnDataSet removes/re-adds these around value assignment)
        _folderBrowserArgsTextBox.TextChanged += OnFolderBrowserArgsChanged;
        _defaultSettingsPathTextBox.TextChanged += OnDefaultSettingsPathChanged;
        // Note: _folderBrowseButton, _settingsBrowseButton, _exportSettingsButton wired in Designer.cs
        // to thin delegators below. Checkbox/numericupdown handlers (autoStart, idleTimeout, autoLock,
        // unlockMode, contextMenu, logging) are wired only in OnDataSet (remove → set value → re-add
        // pattern) — do not wire here.
    }

    protected override void OnDataSet()
    {
        _callerSection.SetSidNames(Database.SidNames, (sid, name) => Database.UpdateSidName(sid, name));

        // Load settings via data loader (may enforce license restrictions on Database.Settings)
        var (state, settingsChangedByLicense) = _dataLoader.LoadSettings(Database.Settings);

        // Apply loaded state to controls (remove handlers to avoid spurious saves during population)
        _autoStartCheckBox.CheckedChanged -= OnAutoStartChanged;
        _idleTimeoutCheckBox.CheckedChanged -= OnIdleTimeoutCheckChanged;
        _idleTimeoutUpDown.ValueChanged -= OnIdleTimeoutChanged;
        _autoLockCheckBox.CheckedChanged -= OnAutoLockCheckChanged;
        _autoLockTimeoutUpDown.ValueChanged -= OnAutoLockTimeoutChanged;

        _autoStartCheckBox.Checked = state.AutoStartEnabled;
        _idleTimeoutCheckBox.Checked = state.IdleTimeoutEnabled;
        _idleTimeoutUpDown.Value = state.IdleTimeoutMinutes;
        _idleTimeoutUpDown.Enabled = state.IdleTimeoutEnabled;
        _autoLockCheckBox.Checked = state.AutoLockEnabled;
        _autoLockTimeoutUpDown.Value = state.AutoLockTimeoutMinutes;
        _autoLockTimeoutUpDown.Enabled = state.AutoLockEnabled;

        _autoStartCheckBox.CheckedChanged += OnAutoStartChanged;
        _idleTimeoutCheckBox.CheckedChanged += OnIdleTimeoutCheckChanged;
        _idleTimeoutUpDown.ValueChanged += OnIdleTimeoutChanged;
        _autoLockCheckBox.CheckedChanged += OnAutoLockCheckChanged;
        _autoLockTimeoutUpDown.ValueChanged += OnAutoLockTimeoutChanged;

        _folderBrowserArgsTextBox.TextChanged -= OnFolderBrowserArgsChanged;
        _defaultSettingsPathTextBox.TextChanged -= OnDefaultSettingsPathChanged;
        _folderBrowserExeTextBox.Text = state.FolderBrowserExePath;
        _folderBrowserArgsTextBox.Text = state.FolderBrowserArguments;
        _defaultSettingsPathTextBox.Text = state.DefaultDesktopSettingsPath;
        _folderBrowserArgsTextBox.TextChanged += OnFolderBrowserArgsChanged;
        _defaultSettingsPathTextBox.TextChanged += OnDefaultSettingsPathChanged;

        _unlockModeComboBox.SelectedIndexChanged -= OnUnlockModeComboChanged;
        _unlockModeComboBox.SelectedIndex = state.UnlockModeIndex;
        _unlockModeComboBox.SelectedIndexChanged += OnUnlockModeComboChanged;

        _contextMenuCheckBox.CheckedChanged -= OnContextMenuChanged;
        _contextMenuCheckBox.Checked = state.EnableContextMenu;
        _contextMenuCheckBox.CheckedChanged += OnContextMenuChanged;

        _enableLoggingCheckBox.CheckedChanged -= OnEnableLoggingChanged;
        _enableLoggingCheckBox.Checked = state.EnableLogging;
        _enableLoggingCheckBox.CheckedChanged += OnEnableLoggingChanged;

        _dragBridgeSection.LoadFromSettings(Database.Settings);

        _blockIcmpCheckBox.CheckedChanged -= _icmpSection.OnBlockIcmpChanged;
        _blockIcmpCheckBox.Checked = Database.Settings.BlockIcmpWhenInternetBlocked;
        _blockIcmpCheckBox.CheckedChanged += _icmpSection.OnBlockIcmpChanged;

        if (settingsChangedByLicense)
            SaveSettings();

        RefreshCallerList();
        _configSection.RefreshConfigList();
    }

    // --- Settings handlers ---

    private async void OnAutoStartChanged(object? sender, EventArgs e)
    {
        try
        {
            await _autoFeatureHandler.SetAutoStart(_autoStartCheckBox.Checked, Database.Settings);
            SaveSettings();
        }
        catch (Exception ex)
        {
            _log.Error("Failed to toggle auto-start", ex);
            MessageBox.Show($"Failed to update auto-start: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OnIdleTimeoutCheckChanged(object? sender, EventArgs e)
    {
        if (!_autoFeatureHandler.SetIdleTimeout(_idleTimeoutCheckBox.Checked, (int)_idleTimeoutUpDown.Value, Database.Settings))
        {
            _idleTimeoutCheckBox.Checked = false;
            MessageBox.Show("Idle timeout requires a license.", "License Required",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _idleTimeoutUpDown.Enabled = _idleTimeoutCheckBox.Checked;
        SaveSettings();
    }

    private void OnIdleTimeoutChanged(object? sender, EventArgs e)
    {
        Database.Settings.IdleTimeoutMinutes = _idleTimeoutCheckBox.Checked ? (int)_idleTimeoutUpDown.Value : 0;
        DebounceSave();
    }

    private void OnAutoLockCheckChanged(object? sender, EventArgs e)
        => _checkboxHandler.OnAutoLockChanged(_autoLockCheckBox.Checked);

    private void OnAutoLockTimeoutChanged(object? sender, EventArgs e)
    {
        _checkboxHandler.OnAutoLockTimeoutChanged((int)_autoLockTimeoutUpDown.Value);
        DebounceSave();
    }

    private void OnContextMenuChanged(object? sender, EventArgs e)
    {
        Database.Settings.EnableRunAsContextMenu = _contextMenuCheckBox.Checked;
        try
        {
            if (_contextMenuCheckBox.Checked)
                _contextMenuService.Register();
            else
                _contextMenuService.Unregister();
        }
        catch (Exception ex)
        {
            _log.Error("Failed to update context menu registration", ex);
        }

        SaveSettings();
    }

    private void OnEnableLoggingChanged(object? sender, EventArgs e)
    {
        Database.Settings.EnableLogging = _enableLoggingCheckBox.Checked;
        _log.Enabled = _enableLoggingCheckBox.Checked;
        SaveSettings();
    }

    private void OnOpenLogClick(object? sender, EventArgs e)
    {
        _log.Info($"OpenLog: identity={System.Security.Principal.WindowsIdentity.GetCurrent().Name}, logPath={_log.LogFilePath}, constantsPath={Constants.LogFilePath}");
        try
        {
            // on some configurations it fails to launch as basic. mystery. use elevated.
            _launchFacade.LaunchFile(_log.LogFilePath, AccountLaunchIdentity.CurrentAccountElevated)?.Dispose();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to open log file: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    public void PerformExportSettings() => _folderBrowserSection.PerformExportSettings();

    private void OnFolderBrowserArgsChanged(object? sender, EventArgs e)
        => _folderBrowserSection.SetArguments(_folderBrowserArgsTextBox.Text);

    private void OnDefaultSettingsPathChanged(object? sender, EventArgs e)
        => _folderBrowserSection.SetDefaultSettingsPath(_defaultSettingsPathTextBox.Text);

    private async void OnUnlockModeComboChanged(object? sender, EventArgs e)
    {
        var newMode = (UnlockMode)_unlockModeComboBox.SelectedIndex;
        bool accepted = await _checkboxHandler.OnUnlockModeChanged(newMode);
        if (!accepted)
        {
            _unlockModeComboBox.SelectedIndexChanged -= OnUnlockModeComboChanged;
            _unlockModeComboBox.SelectedIndex = (int)Database.Settings.UnlockMode;
            _unlockModeComboBox.SelectedIndexChanged += OnUnlockModeComboChanged;
        }
    }

    private void OnFolderBrowserBrowseClick(object? sender, EventArgs e)
        => _folderBrowserSection.BrowseExe();

    private void OnDefaultSettingsPathBrowseClick(object? sender, EventArgs e)
        => _folderBrowserSection.BrowseDesktopSettings();

    private void OnExportDesktopSettingsClick(object? sender, EventArgs e)
        => _folderBrowserSection.ExportDesktopSettingsAsync();

    // --- PIN handlers ---

    private void OnChangePinClick(object? sender, EventArgs e)
    {
        _pinChangeHandler.Run(Session, (oldBuffer, newStore, newKey) =>
            PinDerivedKeyChanged?.Invoke(oldBuffer, newStore, newKey));
    }

    private void OnCleanupClick(object? sender, EventArgs e)
    {
        var result = MessageBox.Show(
            "This will revert ALL ACLs and shortcuts for all managed apps, then close the application.\n\nContinue?",
            "Cleanup & Exit",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);

        if (result == DialogResult.Yes)
        {
            CleanupRequested?.Invoke();
        }
    }

    private async void OnSecurityCheckClick(object? sender, EventArgs e)
        => await _securityCheckHandler.RunAsync(this, _operationGuard);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (_parentForm != null)
            return;
        _parentForm = FindForm();
        if (_parentForm == null)
            return;
        _folderBrowserSection.AttachParentForm(_parentForm);
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        base.OnHandleDestroyed(e);
        if (_parentForm == null)
            return;
        _folderBrowserSection.DetachParentForm();
        _parentForm = null;
    }

    private void OnDragBridgeSettingsChanged()
    {
        _dragBridgeSection.SaveToSettings(Database.Settings);
        SaveSettings();
        DragBridgeSettingsChanged?.Invoke();
    }

    private void OnCallerChanged()
    {
        // Sync the section's items back to the database
        foreach (var a in Database.Accounts)
            a.IsIpcCaller = false;
        foreach (var sid in _callerSection.GetCallers())
            Database.GetOrCreateAccount(sid).IsIpcCaller = true;
        foreach (var a in Database.Accounts.ToList())
            Database.RemoveAccountIfEmpty(a.Sid);
        SaveCallerChanges();
    }

    private void SaveCallerChanges()
    {
        _settingsHandler.SaveCallerChanges(RefreshCallerList, () => DataChanged?.Invoke());
    }

    // --- Caller list handlers ---

    private void RefreshCallerList()
    {
        _callerSection.SetCallers(Database.Accounts.Where(a => a.IsIpcCaller).Select(a => a.Sid).ToList());
    }

    // --- Helpers ---

    private void DebounceSave()
    {
        _settingsHandler.DebounceSave(_pendingEditNotifier.NotifyPendingEdit);
    }

    private void SaveSettings()
    {
        _settingsHandler.SaveSettings(() => SettingsChanged?.Invoke());
    }
}
