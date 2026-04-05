using System.Diagnostics;
using RunFence.Acl.Permissions;
using RunFence.Apps;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.DragBridge.UI.Forms;
using RunFence.Infrastructure;
using RunFence.Licensing;
using RunFence.Persistence;
using RunFence.Persistence.UI;
using RunFence.Persistence.UI.Forms;
using RunFence.Security;
using RunFence.Startup.UI;

namespace RunFence.UI.Forms;

public partial class OptionsPanel : DataPanel
{
    private Form? _parentForm;
    private bool _committingExePath;

    private readonly OptionsSettingsHandler _settingsHandler;
    private readonly IContextMenuService _contextMenuService;
    private readonly ILoggingService _log;
    private readonly OperationGuard _operationGuard = new();
    private readonly PinChangeOrchestrator _pinChangeHandler;
    private readonly SecurityCheckRunner _securityCheckHandler;
    private readonly IWindowsHelloService _windowsHello;
    private readonly OptionsAutoFeatureHandler _autoFeatureHandler;
    private readonly OptionsFolderBrowserHandler _folderBrowserHandler;
    private readonly OptionsDesktopSettingsHandler _desktopSettingsHandler;
    private readonly OptionsPanelDataLoader _dataLoader;

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
        OptionsSettingsHandler settingsHandler,
        ILocalUserProvider localUserProvider,
        IAppConfigService appConfigService,
        IAppFilter appFilter,
        IContextMenuService contextMenuService,
        PinChangeOrchestrator pinChangeOrchestrator,
        SecurityCheckRunner securityCheckRunner,
        ILoggingService log,
        OptionsAutoFeatureHandler autoFeatureHandler,
        OptionsFolderBrowserHandler folderBrowserHandler,
        OptionsDesktopSettingsHandler desktopSettingsHandler,
        OptionsPanelDataLoader dataLoader,
        IAclPermissionService aclPermission,
        ISidEntryHelper sidEntryHelper,
        SidDisplayNameResolver displayNameResolver,
        ILicenseService licenseService,
        IWindowsHelloService windowsHello,
        HandlerSyncHelper? handlerSyncHelper = null)
    {
        _settingsHandler = settingsHandler;
        _contextMenuService = contextMenuService;
        _log = log;
        _pinChangeHandler = pinChangeOrchestrator;
        _securityCheckHandler = securityCheckRunner;
        _windowsHello = windowsHello;
        _autoFeatureHandler = autoFeatureHandler;
        _folderBrowserHandler = folderBrowserHandler;
        _desktopSettingsHandler = desktopSettingsHandler;
        _dataLoader = dataLoader;

        _callerSection = new IpcCallerSection(() => localUserProvider.GetLocalUserAccounts(), sidEntryHelper, displayNameResolver);
        _callerSection.SetShowModalDialog(dlg => ShowModalDialog(dlg));
        _callerSection.Changed += OnCallerChanged;

        _configSection = new ConfigManagerSection(appConfigService, appFilter, log, aclPermission, licenseService, handlerSyncHelper);
        _configSection.ConfigLoadRequested += path => ConfigLoadRequested?.Invoke(path);
        _configSection.ConfigUnloadRequested += path => ConfigUnloadRequested?.Invoke(path);
        _configSection.DataChanged += () => DataChanged?.Invoke();

        _dragBridgeSection = new DragBridgeSection();
        _dragBridgeSection.Changed += OnDragBridgeSettingsChanged;

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

        // TextChanged handlers (OnDataSet removes/re-adds these around value assignment)
        _folderBrowserExeTextBox.Leave += OnFolderBrowserExePathLeave;
        _folderBrowserArgsTextBox.TextChanged += OnFolderBrowserArgsChanged;
        _defaultSettingsPathTextBox.TextChanged += OnDefaultSettingsPathChanged;
        // Note: checkbox/numericupdown handlers (autoStart, idleTimeout, autoLock, unlockMode, contextMenu, logging)
        // are wired only in OnDataSet (remove → set value → re-add pattern) — do not wire here.

        // Add section UserControls to their containers
        _dragBridgeSection.Dock = DockStyle.Fill;
        _dragBridgePlaceholder.Controls.Add(_dragBridgeSection);

        _callerSection.Dock = DockStyle.Fill;
        _callerGroup.Controls.Add(_callerSection);

        _configSection.Dock = DockStyle.Fill;
        _rightConfigPanel.Controls.Add(_configSection);
    }

    protected override void OnDataSet()
    {
        // Set data context for ConfigManagerSection
        _configSection.SetDataContext(() => Database, () => CredentialStore, () => PinDerivedKey);
        _callerSection.SetSidNames(Database.SidNames, (sid, name) => Database.UpdateSidName(sid, name));

        // Load settings via data loader (may enforce license restrictions on Database.Settings)
        var (state, settingsChangedByLicense) = _dataLoader.LoadSettings(Database.Settings);

        // Apply loaded state to controls (remove handlers to avoid spurious saves during population)
        _autoStartCheckBox.CheckedChanged -= OnAutoStartChanged;
        _idleTimeoutCheckBox.CheckedChanged -= OnIdleTimeoutCheckChanged;
        _idleTimeoutUpDown.ValueChanged -= OnIdleTimeoutChanged;
        _autoLockCheckBox.CheckedChanged -= OnAutoLockChanged;
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
        _autoLockCheckBox.CheckedChanged += OnAutoLockChanged;
        _autoLockTimeoutUpDown.ValueChanged += OnAutoLockTimeoutChanged;

        _folderBrowserArgsTextBox.TextChanged -= OnFolderBrowserArgsChanged;
        _defaultSettingsPathTextBox.TextChanged -= OnDefaultSettingsPathChanged;
        _folderBrowserExeTextBox.Text = state.FolderBrowserExePath;
        _folderBrowserArgsTextBox.Text = state.FolderBrowserArguments;
        _defaultSettingsPathTextBox.Text = state.DefaultDesktopSettingsPath;
        _folderBrowserArgsTextBox.TextChanged += OnFolderBrowserArgsChanged;
        _defaultSettingsPathTextBox.TextChanged += OnDefaultSettingsPathChanged;

        _unlockModeComboBox.SelectedIndexChanged -= OnUnlockModeChanged;
        _unlockModeComboBox.SelectedIndex = state.UnlockModeIndex;
        _unlockModeComboBox.SelectedIndexChanged += OnUnlockModeChanged;

        _contextMenuCheckBox.CheckedChanged -= OnContextMenuChanged;
        _contextMenuCheckBox.Checked = state.EnableContextMenu;
        _contextMenuCheckBox.CheckedChanged += OnContextMenuChanged;

        _enableLoggingCheckBox.CheckedChanged -= OnEnableLoggingChanged;
        _enableLoggingCheckBox.Checked = state.EnableLogging;
        _enableLoggingCheckBox.CheckedChanged += OnEnableLoggingChanged;

        _dragBridgeSection.LoadFromSettings(Database.Settings);

        if (settingsChangedByLicense)
            SaveSettings();

        RefreshCallerList();
        _configSection.RefreshConfigList();
    }

    // --- Settings handlers ---

    private void OnAutoStartChanged(object? sender, EventArgs e)
    {
        try
        {
            _autoFeatureHandler.SetAutoStart(_autoStartCheckBox.Checked, Database.Settings);
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

    private void OnAutoLockChanged(object? sender, EventArgs e)
    {
        if (!_autoFeatureHandler.SetAutoLock(_autoLockCheckBox.Checked, Database.Settings))
        {
            _autoLockCheckBox.Checked = false;
            MessageBox.Show("Auto-lock requires a license.", "License Required",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _autoLockTimeoutUpDown.Enabled = _autoLockCheckBox.Checked;
        SaveSettings();
    }

    private void OnAutoLockTimeoutChanged(object? sender, EventArgs e)
    {
        Database.Settings.AutoLockTimeoutMinutes = (int)_autoLockTimeoutUpDown.Value;
        DebounceSave();
    }

    private async void OnUnlockModeChanged(object? sender, EventArgs e)
    {
        var newMode = (UnlockMode)_unlockModeComboBox.SelectedIndex;
        if (newMode == UnlockMode.WindowsHello)
        {
            bool available = await _windowsHello.IsAvailableAsync();
            if (!available)
            {
                _unlockModeComboBox.SelectedIndexChanged -= OnUnlockModeChanged;
                _unlockModeComboBox.SelectedIndex = (int)Database.Settings.UnlockMode;
                _unlockModeComboBox.SelectedIndexChanged += OnUnlockModeChanged;
                MessageBox.Show(
                    "Windows Hello is not available or not configured for this account.",
                    "Windows Hello Unavailable",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }
        }

        Database.Settings.UnlockMode = newMode;
        SaveSettings();
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
            Process.Start(new ProcessStartInfo("cmd.exe")
            {
                Arguments = $"/c start \"\" \"{_log.LogFilePath}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to open log file: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (_parentForm != null)
            return;
        _parentForm = FindForm();
        if (_parentForm == null)
            return;
        _parentForm.Deactivate += OnParentFormCommitTrigger;
        _parentForm.FormClosing += OnParentFormCommitTrigger;
        _parentForm.SizeChanged += OnParentFormSizeChanged;
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        base.OnHandleDestroyed(e);
        if (_parentForm == null)
            return;
        _parentForm.Deactivate -= OnParentFormCommitTrigger;
        _parentForm.FormClosing -= OnParentFormCommitTrigger;
        _parentForm.SizeChanged -= OnParentFormSizeChanged;
        _parentForm = null;
    }

    private void OnParentFormCommitTrigger(object? sender, EventArgs e) => CommitFolderBrowserExePath();

    private void OnParentFormSizeChanged(object? sender, EventArgs e)
    {
        if (FindForm()?.WindowState == FormWindowState.Minimized)
            CommitFolderBrowserExePath();
    }

    private void OnFolderBrowserExePathLeave(object? sender, EventArgs e) => CommitFolderBrowserExePath();

    private void CommitFolderBrowserExePath()
    {
        if (_committingExePath)
            return;
        _committingExePath = true;
        try
        {
            var error = _folderBrowserHandler.ValidateAndCommitExePath(_folderBrowserExeTextBox.Text, Database.Settings);
            if (error != null)
            {
                MessageBox.Show(error, "Unsupported Application", MessageBoxButtons.OK, MessageBoxIcon.Error);
                _folderBrowserExeTextBox.Text = Database.Settings.FolderBrowserExePath;
                return;
            }

            DebounceSave();
        }
        finally
        {
            _committingExePath = false;
        }
    }

    private void OnFolderBrowserArgsChanged(object? sender, EventArgs e)
    {
        _folderBrowserHandler.SetArguments(_folderBrowserArgsTextBox.Text, Database.Settings);
        DebounceSave();
    }

    private void OnFolderBrowserBrowseClick(object? sender, EventArgs e)
    {
        var path = _folderBrowserHandler.BrowseExe();
        if (path != null)
        {
            _folderBrowserExeTextBox.Text = path;
            OnFolderBrowserExePathLeave(sender, e);
        }
    }

    private void OnDefaultSettingsPathChanged(object? sender, EventArgs e)
    {
        _desktopSettingsHandler.SetDesktopSettingsPath(_defaultSettingsPathTextBox.Text, Database.Settings);
        DebounceSave();
    }

    public void PerformExportSettings() => _exportSettingsButton.PerformClick();

    private void OnDefaultSettingsPathBrowseClick(object? sender, EventArgs e)
    {
        var path = _desktopSettingsHandler.BrowseDesktopSettings();
        if (path != null)
            _defaultSettingsPathTextBox.Text = path;
    }

    private async void OnExportDesktopSettingsClick(object? sender, EventArgs e)
    {
        using var dlg = new SaveFileDialog();
        dlg.Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*";
        dlg.DefaultExt = "json";
        dlg.FileName = "settings.json";
        dlg.Title = "Export Desktop Settings";
        try
        {
            Directory.CreateDirectory(Constants.ProgramDataDir);
            dlg.InitialDirectory = Constants.ProgramDataDir;
        }
        catch
        {
        }

        FileDialogHelper.AddInteractiveUserCustomPlaces(dlg);

        if (dlg.ShowDialog() != DialogResult.OK)
            return;

        var outputPath = dlg.FileName;
        _operationGuard.Begin(this);
        BeginModal();
        try
        {
            await _desktopSettingsHandler.ExportAsync(outputPath);

            if (IsDisposed)
                return;

            if (string.IsNullOrEmpty(_defaultSettingsPathTextBox.Text))
                _defaultSettingsPathTextBox.Text = outputPath;

            var openForEdit = MessageBox.Show("Desktop settings exported successfully.\n\nOpen file for editing?",
                "Success", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
            if (openForEdit == DialogResult.Yes)
                _desktopSettingsHandler.OpenForEdit(outputPath);
        }
        catch (Exception ex)
        {
            if (IsDisposed)
                return;
            _log.Error("Desktop settings export failed", ex);
            MessageBox.Show($"Export failed: {ex.Message}",
                "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            EndModal();
            _operationGuard.End(this);
        }
    }

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
            CleanupRequested?.Invoke();
    }

    private async void OnSecurityCheckClick(object? sender, EventArgs e)
        => await _securityCheckHandler.RunAsync(this, _operationGuard);

    // --- Caller list handlers ---

    private void RefreshCallerList()
    {
        _callerSection.SetCallers(Database.Accounts.Where(a => a.IsIpcCaller).Select(a => a.Sid).ToList());
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