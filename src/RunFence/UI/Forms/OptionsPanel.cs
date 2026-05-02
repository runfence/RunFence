using RunFence.Apps;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.DragBridge.UI.Forms;
using RunFence.Infrastructure;
using RunFence.Launch;
using RunFence.Persistence.UI.Forms;
using RunFence.Startup;
using RunFence.Startup.UI;

namespace RunFence.UI.Forms;

/// <remarks>Deps above threshold: 18 deps: all are already-extracted independent feature sections/handlers. Tab-based split would create artificial cross-tab state sharing. Each handler already owns its own concern. Reviewed 2026-04-20.</remarks>
public partial class OptionsPanel : DataPanel, IDragBridgeSettingsChangeSource
{
    private static readonly LogVerbosity[] LogVerbosityComboValues =
    {
        LogVerbosity.Off,
        LogVerbosity.Error,
        LogVerbosity.Warning,
        LogVerbosity.Info,
        LogVerbosity.Debug
    };

    private Form? _parentForm;

    private readonly OptionsSettingsHandler _settingsHandler;
    private readonly IContextMenuService _contextMenuService;
    private readonly ILoggingService _log;
    private readonly ILaunchFacade _launchFacade;
    private readonly OperationGuard _operationGuard = new();
    private readonly PinChangeOrchestrator _pinChangeHandler;
    private readonly SecurityCheckRunner _securityCheckHandler;
    private readonly IAutoStartService _autoStartService;
    private readonly OptionsStartWithoutPinHandler _startWithoutPinHandler;
    private readonly OptionsPanelDataLoader _dataLoader;
    private readonly OptionsIcmpSection _icmpSection;
    private readonly OptionsFolderBrowserSection _folderBrowserSection;
    private readonly OptionsPanelCheckboxHandler _checkboxHandler;
    private readonly IAssociationAutoSetService _autoSetService;
    private readonly AccountConfigTransferOrchestrator _migrateOrchestrator;

    private readonly IpcCallerSection _callerSection;
    private readonly ConfigManagerSection _configSection;
    private readonly DragBridgeSection _dragBridgeSection;

    public event Action? SettingsChanged;
    public event Action? DataChanged;
    public event Action<ProtectedBuffer, CredentialStore, ProtectedBuffer>? PinDerivedKeyChanged;
    public event Action? CleanupRequested;
    public event Action? MigrationExitRequested;
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
        IAutoStartService autoStartService,
        OptionsStartWithoutPinHandler startWithoutPinHandler,
        ILaunchFacade launchFacade,
        OptionsPanelDataLoader dataLoader,
        OptionsIcmpSection icmpSection,
        OptionsFolderBrowserSection folderBrowserSection,
        OptionsPanelCheckboxHandler checkboxHandler,
        IAssociationAutoSetService autoSetService,
        AccountConfigTransferOrchestrator migrateOrchestrator,
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
        _autoStartService = autoStartService;
        _startWithoutPinHandler = startWithoutPinHandler;
        _dataLoader = dataLoader;
        _icmpSection = icmpSection;
        _folderBrowserSection = folderBrowserSection;
        _checkboxHandler = checkboxHandler;
        _autoSetService = autoSetService;
        _migrateOrchestrator = migrateOrchestrator;

        _callerSection = ipcCallerSectionFactory();
        _callerSection.SetShowModalDialog(dlg => ShowModalDialog(dlg));
        _callerSection.Changed += OnCallerChanged;

        _configSection = configManagerSectionFactory();
        _configSection.ConfigLoadRequested += path => ConfigLoadRequested?.Invoke(path);
        _configSection.ConfigUnloadRequested += path => ConfigUnloadRequested?.Invoke(path);
        _configSection.DataChanged += () => DataChanged?.Invoke();

        _dragBridgeSection = dragBridgeSectionFactory();
        _dragBridgeSection.Changed += OnDragBridgeSettingsChanged;

        InitializeComponent();

        _logVerbosityComboBox.Items.AddRange(LogVerbosityComboValues.Cast<object>().ToArray());

        // Set inline browse button heights to match adjacent text boxes for DPI correctness
        _folderBrowseButton.Height = _folderBrowserExeTextBox.PreferredHeight;
        _folderBrowseButton.Top = _folderBrowserExeTextBox.Top;
        _settingsBrowseButton.Height = _defaultSettingsPathTextBox.PreferredHeight;
        _settingsBrowseButton.Top = _defaultSettingsPathTextBox.Top;

        // Set images (runtime bitmap generation)
        _changePinBtn.Image = UiIconFactory.CreateToolbarIcon("\u2731", Color.FromArgb(0x33, 0x66, 0x99), 16);
        _cleanupBtn.Image = UiIconFactory.CreateToolbarIcon("\u2716", Color.FromArgb(0x99, 0x33, 0x33), 16);
        _securityCheckBtn.Image = UiIconFactory.CreateToolbarIcon("\u26A0", Color.FromArgb(0x22, 0x8B, 0x22), 16);
        _migrateAccountBtn.Image = UiIconFactory.CreateToolbarIcon("\u2794", Color.FromArgb(0x33, 0x66, 0x99), 16);

        // Tooltips
        _tooltip.SetToolTip(_cleanupBtn, "Reverts all ACLs and shortcuts for all managed apps, then exits");
        _tooltip.SetToolTip(_securityCheckBtn, "Scan startup folders and registry for security issues");
        _tooltip.SetToolTip(_migrateAccountBtn, "Copy config and credentials to another admin account");
        _tooltip.SetToolTip(_autoLockTimeoutUpDown, "Minutes after being sent to tray before RunFence GUI is hidden. IPC remains active. Set to 0 to lock immediately.");
        _blockIcmpCheckBoxTooltip.SetToolTip(_blockIcmpCheckBox, "ICMP tunneling can be potentially used to escape Internet restrictions");
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
        // unlockMode, contextMenu, log verbosity, startWithoutPin) are wired only in OnDataSet (remove → set value
        // → re-add pattern) — do not wire here.
    }

    protected override async void OnDataSet()
    {
        try
        {
            _callerSection.SetSidNames(Database.SidNames, (sid, name) => Database.UpdateSidName(sid, name));

            // Load settings via data loader (may enforce license restrictions on Database.Settings)
            var (state, settingsChangedByLicense) = await _dataLoader.LoadSettingsAsync(Database.Settings);

            if (IsDisposed)
                return;

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

            _logVerbosityComboBox.SelectedIndexChanged -= OnLogVerbosityChanged;
            var logVerbosityIndex = Array.IndexOf(LogVerbosityComboValues, state.LogVerbosity);
            _logVerbosityComboBox.SelectedIndex = logVerbosityIndex >= 0
                ? logVerbosityIndex
                : Array.IndexOf(LogVerbosityComboValues, LogVerbosity.Info);
            _logVerbosityComboBox.SelectedIndexChanged += OnLogVerbosityChanged;

            _startWithoutPinCheckBox.CheckedChanged -= OnStartWithoutPinChanged;
            _startWithoutPinCheckBox.Checked = _startWithoutPinHandler.IsStartWithoutPinEnabled;
            _startWithoutPinCheckBox.CheckedChanged += OnStartWithoutPinChanged;

            _dragBridgeSection.LoadFromSettings(Database.Settings);

            _blockIcmpCheckBox.CheckedChanged -= _icmpSection.OnBlockIcmpChanged;
            _blockIcmpCheckBox.Checked = Database.Settings.BlockIcmpWhenInternetBlocked;
            _blockIcmpCheckBox.CheckedChanged += _icmpSection.OnBlockIcmpChanged;

            if (settingsChangedByLicense)
                SaveSettings();

            RefreshCallerList();
            _configSection.RefreshConfigList();
        }
        catch (Exception ex)
        {
            _log.Error("OptionsPanel.OnDataSet failed", ex);
        }
    }

    // --- Settings handlers ---

    private async void OnAutoStartChanged(object? sender, EventArgs e)
    {
        bool success = false;
        try
        {
            if (_autoStartCheckBox.Checked)
                await _autoStartService.EnableAutoStart();
            else
                await _autoStartService.DisableAutoStart();

            Database.Settings.AutoStartOnLogin = _autoStartCheckBox.Checked;
            success = true;
        }
        catch (Exception ex)
        {
            _log.Error("Failed to toggle auto-start", ex);
            MessageBox.Show($"Failed to update auto-start: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _autoStartCheckBox.CheckedChanged -= OnAutoStartChanged;
            _autoStartCheckBox.Checked = Database.Settings.AutoStartOnLogin;
            _autoStartCheckBox.CheckedChanged += OnAutoStartChanged;
        }

        if (success)
            SaveSettings();
    }

    private void OnStartWithoutPinChanged(object? sender, EventArgs e)
    {
        var requested = _startWithoutPinCheckBox.Checked;
        _startWithoutPinCheckBox.CheckedChanged -= OnStartWithoutPinChanged;
        try
        {
            if (requested && !_startWithoutPinHandler.IsLicensed)
            {
                _startWithoutPinCheckBox.Checked = false;
                MessageBox.Show("This feature requires a license.", "License Required",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            _startWithoutPinHandler.SetStartWithoutPin(
                requested,
                (oldBuffer, newStore, newKey) => PinDerivedKeyChanged?.Invoke(oldBuffer, newStore, newKey));
        }
        finally
        {
            _startWithoutPinCheckBox.Checked = _startWithoutPinHandler.IsStartWithoutPinEnabled;
            _startWithoutPinCheckBox.CheckedChanged += OnStartWithoutPinChanged;
        }
    }

    private void OnIdleTimeoutCheckChanged(object? sender, EventArgs e)
    {
        if (!_checkboxHandler.SetIdleTimeout(_idleTimeoutCheckBox.Checked, (int)_idleTimeoutUpDown.Value, Database.Settings))
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
        _checkboxHandler.SetIdleTimeout(_idleTimeoutCheckBox.Checked, (int)_idleTimeoutUpDown.Value, Database.Settings);
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
        var requested = _contextMenuCheckBox.Checked;
        try
        {
            if (requested)
                _contextMenuService.Register();
            else
                _contextMenuService.Unregister();
        }
        catch (Exception ex)
        {
            _log.Error("Failed to update context menu registration", ex);
            MessageBox.Show($"Failed to update context menu: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            _contextMenuCheckBox.CheckedChanged -= OnContextMenuChanged;
            _contextMenuCheckBox.Checked = Database.Settings.EnableRunAsContextMenu;
            _contextMenuCheckBox.CheckedChanged += OnContextMenuChanged;
            return;
        }

        Database.Settings.EnableRunAsContextMenu = requested;
        SaveSettings();
    }

    private void OnLogVerbosityChanged(object? sender, EventArgs e)
    {
        var index = _logVerbosityComboBox.SelectedIndex;
        var verbosity = index >= 0 && index < LogVerbosityComboValues.Length
            ? LogVerbosityComboValues[index]
            : LogVerbosity.Info;
        Database.Settings.LogVerbosity = verbosity;
        _log.Verbosity = verbosity;
        SaveSettings();
    }

    private void OnOpenLogClick(object? sender, EventArgs e)
    {
        _log.Info($"OpenLog: identity={System.Security.Principal.WindowsIdentity.GetCurrent().Name}, logPath={_log.LogFilePath}, constantsPath={PathConstants.LogFilePath}");
        try
        {
            // on some configs it doesn't work with basic
            // but changing it to elevated enables redirection to interactive user explorer
            // which can only be disabled by deleting HKLM\Software\Classes\AppID\{CDCBCFCA-3CDC-436f-A4E2-0E02075250C2}\RunAs
            _launchFacade.LaunchFile(
                _log.LogFilePath,
                AccountLaunchIdentity.CurrentAccountBasic with
                {
                    AssociationResolutionPolicy = AssociationResolutionPolicy.AllowAccountRedirection
                })?.Dispose();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to open log file: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

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

    private void OnMigrateAccountClick(object? sender, EventArgs e)
    {
        var accounts = _migrateOrchestrator.GetAvailableAccounts();
        if (accounts.Count == 0)
        {
            MessageBox.Show(
                "No other enabled administrator accounts were found.",
                "No Accounts Available",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        var menu = new ContextMenuStrip();
        foreach (var (displayName, sid) in accounts)
        {
            var item = new ToolStripMenuItem(displayName);
            item.Click += async (_, _) =>
            {
                _migrateAccountBtn.Enabled = false;
                try
                {
                    await _migrateOrchestrator.RunAsync(Session, sid, displayName, () =>
                    {
                        _settingsHandler.CancelPendingSave();
                        MigrationExitRequested?.Invoke();
                    });
                }
                finally
                {
                    _migrateAccountBtn.Enabled = true;
                }
            };
            menu.Items.Add(item);
        }
        menu.Show(_migrateAccountBtn, new Point(0, _migrateAccountBtn.Height));
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
        // Capture existing IPC callers before modification to detect newly added ones
        var previousCallers = Database.Accounts
            .Where(a => a.IsIpcCaller)
            .Select(a => a.Sid)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Sync the section's items back to the database
        foreach (var a in Database.Accounts)
            a.IsIpcCaller = false;
        foreach (var sid in _callerSection.GetCallers())
            Database.GetOrCreateAccount(sid).IsIpcCaller = true;
        foreach (var a in Database.Accounts.ToList())
            Database.RemoveAccountIfEmpty(a.Sid);
        SaveCallerChanges();

        // Re-register HKCU associations for newly added global IPC callers
        var newGlobalCallers = Database.Accounts
            .Where(a => a.IsIpcCaller && !previousCallers.Contains(a.Sid));
        foreach (var account in newGlobalCallers)
            _autoSetService.ForceAutoSetForUser(account.Sid);
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
        _settingsHandler.DebounceSave(SaveSettings);
    }

    private void SaveSettings()
    {
        _settingsHandler.SaveSettings(() => SettingsChanged?.Invoke());
    }
}
