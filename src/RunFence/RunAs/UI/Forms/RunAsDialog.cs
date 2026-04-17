using System.Security;
using RunFence.Account;
using RunFence.Account.UI;
using RunFence.Account.UI.AppContainer;
using RunFence.Acl.Permissions;
using RunFence.Acl.UI;
using RunFence.Apps.Shortcuts;
using RunFence.Apps.UI;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Launch;
using RunFence.UI;

namespace RunFence.RunAs.UI.Forms;

public partial class RunAsDialog : Form
{
    private string _filePath = null!;
    private string? _arguments;
    private List<CredentialEntry> _credentials = null!;
    private List<AppEntry> _existingApps = null!;
    private ShortcutContext? _shortcutContext;
    private List<AppContainerEntry>? _appContainers;
    private AppEntry? _currentExistingApp;

    private RunAsDialogState _dialogState = null!;

    private CredentialEntry? SelectedCredential => _dialogState.SelectedCredential;
    private AppContainerEntry? SelectedContainer => _dialogState.SelectedContainer;
    public bool CreateNewAccountRequested => _dialogState.CreateNewAccountRequested;
    public bool CreateNewContainerRequested => _dialogState.CreateNewContainerRequested;
    private AncestorPermissionResult? PermissionGrant => _dialogState.PermissionGrant;
    private bool CreateAppEntryOnly { get; set; }
    private PrivilegeLevel SelectedPrivilegeLevel { get; set; }
    private bool UpdateOriginalShortcut { get; set; }
    private bool RevertShortcutRequested { get; set; }
    private AppEntry? EditExistingApp { get; set; }
    private AppEntry? ExistingAppForLaunch { get; set; }
    private SecureString? AdHocPassword { get; set; }
    private bool RememberPassword { get; set; }

    private string? _lastUsedAccountSid;
    private string? _lastUsedContainerName;
    private string? _currentUserSid;
    private IReadOnlyDictionary<string, string>? _sidNames;
    private IReadOnlyDictionary<string, PrivilegeLevel>? _accountPrivilegeLevels;
    private readonly ISidResolver _sidResolver;
    private readonly IWindowsAccountService _windowsAccountService;
    private readonly RunAsCredentialListPopulator _populator;
    private readonly RunAsCredentialListRenderer _renderer;
    private readonly IAclPermissionService _aclPermission;
    private readonly ToolTip _toolTip = new();

    public RunAsDialog(
        ISidResolver sidResolver,
        IAclPermissionService aclPermission,
        RunAsCredentialListPopulator populator,
        RunAsCredentialListRenderer renderer,
        IWindowsAccountService windowsAccountService)
    {
        _sidResolver = sidResolver;
        _aclPermission = aclPermission;
        _populator = populator;
        _renderer = renderer;
        _windowsAccountService = windowsAccountService;
        InitializeComponent();
        Icon = AppIcons.GetAppIcon();
        _toolTip.SetToolTip(_privilegeLevelComboBox, LaunchUiConstants.PrivilegeLevelTooltip);
    }

    private static readonly PrivilegeLevel[] PrivilegeLevelMapping = [PrivilegeLevel.HighestAllowed, PrivilegeLevel.Basic, PrivilegeLevel.LowIntegrity];

    /// <summary>
    /// Initializes per-use dialog data. Must be called before <see cref="Form.ShowDialog()"/>.
    /// </summary>
    public void Initialize(RunAsDialogOptions options)
    {
        _filePath = options.FilePath;
        _arguments = options.Arguments;
        _credentials = options.Credentials;
        _existingApps = options.ExistingApps;
        _lastUsedAccountSid = options.LastUsedAccountSid;
        _lastUsedContainerName = options.LastUsedContainerName;
        _currentUserSid = options.CurrentUserSid;
        _sidNames = options.SidNames;
        _shortcutContext = options.ShortcutContext;
        _appContainers = options.AppContainers;
        _accountPrivilegeLevels = options.AccountPrivilegeLevels;
        _dialogState = new RunAsDialogState(_filePath, options.SidsNeedingPermission, _aclPermission);

        _populator.Initialize(
            _credentialListBox, _credentials, _sidNames, _showAllAccountsCheckBox,
            _currentUserSid, _appContainers);
        _renderer.Attach(_credentialListBox);

        ConfigureLayout();
        Shown += (_, _) => Activate();
    }

    /// <summary>
    /// Captures all dialog output into a <see cref="RunAsDialogResult"/> after <see cref="DialogResult.OK"/>.
    /// </summary>
    public RunAsDialogResult CaptureResult() => new(
        SelectedCredential,
        SelectedContainer,
        PermissionGrant,
        CreateAppEntryOnly,
        SelectedPrivilegeLevel,
        UpdateOriginalShortcut,
        RevertShortcutRequested,
        EditExistingApp,
        ExistingAppForLaunch,
        AdHocPassword,
        RememberPassword);

    private void ConfigureLayout()
    {
        var y = 10;

        _pathHeaderLabel.Location = new Point(15, y);
        y += 22;

        _pathLabel.Text = _filePath;
        _pathLabel.Location = new Point(15, y);
        y += _pathLabel.PreferredSize.Height + 4;

        if (_shortcutContext != null)
        {
            var lnkName = Path.GetFileName(_shortcutContext.OriginalLnkPath);
            _shortcutLabel.Text = _shortcutContext.IsAlreadyManaged
                ? $"(managed shortcut: {lnkName})"
                : $"(from shortcut: {lnkName})";
            _shortcutLabel.Location = new Point(15, y);
            _shortcutLabel.Visible = true;
            y += 20;
        }

        if (!string.IsNullOrEmpty(_arguments))
        {
            _argsLabel.Location = new Point(15, y);
            _argsLabel.Visible = true;
            y += 20;

            _argsTextBox.Text = _arguments;
            _argsTextBox.Location = new Point(15, y);
            _argsTextBox.Visible = true;
            y += 30;
        }

        _credLabel.Location = new Point(15, y);
        y += 20;

        _showAllAccountsCheckBox.Location = new Point(15, y);
        _showAllAccountsCheckBox.Visible = true;
        y += 22;

        _credentialListBox.Location = new Point(15, y);
        RepopulateCredentialList();
        y += 217;

        if (_shortcutContext is { IsAlreadyManaged: false })
        {
            _updateShortcutCheckBox.Location = new Point(15, y);
            _updateShortcutCheckBox.Visible = true;
            y += 25;
        }

        _privilegeLevelLabel.Location = new Point(15, y + 5);
        _privilegeLevelComboBox.Location = new Point(265, y);
        y += 37;

        if (_shortcutContext is { IsAlreadyManaged: true, ManagedApp: not null })
        {
            _revertButton.Location = new Point(15, y);
            _revertButton.Visible = true;
        }

        _launchButton.Location = new Point(155, y);
        _addAppButton.Location = new Point(250, y);
        _cancelButton.Location = new Point(375, y);

        // Enable/disable "Add App..." based on credential selection
        _credentialListBox.SelectedIndexChanged += (_, _) =>
            _addAppButton.Enabled = _credentialListBox.SelectedItem is CredentialDisplayItem or CreateAccountItem or AppContainerDisplayItem;

        ClientSize = ClientSize with { Height = y + 45 };

        // Pre-select: managed app's account/container takes priority; otherwise last used or first non-current
        int initialSelection = _shortcutContext?.IsAlreadyManaged == true
            ? FindManagedAppSelection()
            : FindPreferredSelectionForNewApp();

        if (_credentialListBox.Items.Count > 0)
            _credentialListBox.SelectedIndex = initialSelection;
    }

    private int FindManagedAppSelection()
    {
        var preferSid = _shortcutContext!.ManagedApp?.AccountSid;
        if (preferSid != null)
        {
            var idx = _populator.FindItemIndex(item => item is CredentialDisplayItem di &&
                                            string.Equals(di.Credential.Sid, preferSid, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
                return idx;
        }

        var containerName = _shortcutContext.ManagedApp?.AppContainerName;
        if (containerName != null)
        {
            var idx = _populator.FindItemIndex(item => item is AppContainerDisplayItem acdi &&
                                            string.Equals(acdi.Container.Name, containerName, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
                return idx;
        }

        return 0;
    }

    private int FindPreferredSelectionForNewApp()
    {
        // Try last used account (skip if it's the current elevated user)
        if (_lastUsedAccountSid != null &&
            !string.Equals(_lastUsedAccountSid, _currentUserSid, StringComparison.OrdinalIgnoreCase))
        {
            var idx = _populator.FindItemIndex(item => item is CredentialDisplayItem di &&
                                            string.Equals(di.Credential.Sid, _lastUsedAccountSid, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
                return idx;
        }

        // Try last used container
        if (_lastUsedContainerName != null)
        {
            var idx = _populator.FindItemIndex(item => item is AppContainerDisplayItem acdi &&
                                            string.Equals(acdi.Container.Name, _lastUsedContainerName, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
                return idx;
        }

        // Fall back to first non-current-user selectable item
        var fallback = _populator.FindItemIndex(item =>
            item is AppContainerDisplayItem ||
            (item is CredentialDisplayItem di2 &&
             !string.Equals(di2.Credential.Sid, _currentUserSid, StringComparison.OrdinalIgnoreCase)));
        return fallback >= 0 ? fallback : 0;
    }

    private void OnRevertClick(object? sender, EventArgs e)
    {
        RevertShortcutRequested = true;
        DialogResult = DialogResult.OK;
        Close();
    }

    private void OnAddAppClick(object? sender, EventArgs e)
    {
        CreateAppEntryOnly = true;
        EditExistingApp = _currentExistingApp; // null = new, non-null = edit existing
        if (!CaptureSelectionState())
            return;
        if (SelectedCredential == null && !CreateNewAccountRequested && SelectedContainer == null && !CreateNewContainerRequested)
            return;
        if (!TryPromptAdHocPassword())
            return;
        DialogResult = DialogResult.OK;
        Close();
    }

    private void OnCancelClick(object? sender, EventArgs e)
    {
        DialogResult = DialogResult.Cancel;
        Close();
    }

    private void OnUpdateShortcutCheckBoxChanged(object? sender, EventArgs e)
    {
        UpdateLaunchButtonState();
    }

    private int _lastCredentialListIndex = -1;

    private void OnCredentialSelectionChanged(object? sender, EventArgs e)
    {
        if (SeparatorSkipHelper.HandleSeparatorSkip(
                _credentialListBox.SelectedItem,
                _credentialListBox.SelectedIndex,
                _credentialListBox.Items.Count,
                i => _credentialListBox.SelectedIndex = i,
                ref _lastCredentialListIndex))
            return;

        switch (_credentialListBox.SelectedItem)
        {
            case CreateAccountItem:
                _currentExistingApp = null;
                SetPrivilegeLevel(PrivilegeLevel.Basic, enabled: true);
                UpdateLaunchButtonState();
                _addAppButton.Text = "Add app entry\u2026";
                return;
            case CreateContainerItem:
                _currentExistingApp = null;
                SetPrivilegeLevel(PrivilegeLevel.LowIntegrity, enabled: false);
                _addAppButton.Enabled = false;
                UpdateLaunchButtonState();
                return;
            case AppContainerDisplayItem containerItem:
            {
                // Check for existing app entry with this container + path
                var existingApp = _existingApps.FirstOrDefault(a =>
                    string.Equals(a.AppContainerName, containerItem.Container.Name, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(a.ExePath, _filePath, StringComparison.OrdinalIgnoreCase));

                _currentExistingApp = existingApp;
                SetPrivilegeLevel(PrivilegeLevel.LowIntegrity, enabled: false);
                _addAppButton.Text = existingApp != null ? "Edit app entry\u2026" : "Add app entry\u2026";
                UpdateLaunchButtonState();
                return;
            }
            case CredentialDisplayItem item:
            {
                var existingApp = _existingApps.FirstOrDefault(a =>
                    string.Equals(a.AccountSid, item.Credential.Sid, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(a.ExePath, _filePath, StringComparison.OrdinalIgnoreCase));

                if (existingApp != null)
                {
                    _currentExistingApp = existingApp;
                    var resolvedMode = existingApp.PrivilegeLevel
                        ?? _accountPrivilegeLevels?.GetValueOrDefault(item.Credential.Sid, PrivilegeLevel.Basic)
                        ?? PrivilegeLevel.Basic;
                    SetPrivilegeLevel(resolvedMode, enabled: false);
                }
                else
                {
                    _currentExistingApp = null;
                    var accountMode = _accountPrivilegeLevels?.GetValueOrDefault(item.Credential.Sid, PrivilegeLevel.Basic)
                        ?? PrivilegeLevel.Basic;
                    SetPrivilegeLevel(accountMode, enabled: true);
                }

                break;
            }
            default:
                _currentExistingApp = null;
                SetPrivilegeLevel(PrivilegeLevel.Basic, enabled: false);
                break;
        }

        _addAppButton.Text = _currentExistingApp != null ? "Edit app entry\u2026" : "Add app entry\u2026";
        UpdateLaunchButtonState();
    }

    private void UpdateLaunchButtonState()
    {
        var hasCredential = _credentialListBox.SelectedItem is CredentialDisplayItem or CreateAccountItem or AppContainerDisplayItem or CreateContainerItem;
        var shortcutBlocked = _updateShortcutCheckBox.Visible && _updateShortcutCheckBox.Checked && _currentExistingApp == null;
        _launchButton.Enabled = hasCredential && !shortcutBlocked;
        _toolTip.SetToolTip(_launchButton, shortcutBlocked
            ? "Uncheck 'Update this shortcut' or add app entry first"
            : null);
    }

    private bool CaptureSelectionState()
    {
        bool ok = _dialogState.ResolveSelectionState(
            _credentialListBox.SelectedItem,
            this,
            _currentExistingApp,
            GetCurrentPrivilegeLevel(),
            _updateShortcutCheckBox?.Checked ?? false,
            out var privilegeLevel,
            out var updateOriginalShortcut,
            out var existingAppForLaunch);

        SelectedPrivilegeLevel = privilegeLevel;
        UpdateOriginalShortcut = updateOriginalShortcut;
        ExistingAppForLaunch = existingAppForLaunch;
        return ok;
    }

    private PrivilegeLevel GetCurrentPrivilegeLevel() => PrivilegeLevelMapping[_privilegeLevelComboBox.SelectedIndex];

    private void SetPrivilegeLevel(PrivilegeLevel mode, bool enabled)
    {
        _privilegeLevelComboBox.SelectedIndex = Array.IndexOf(PrivilegeLevelMapping, mode);
        _privilegeLevelComboBox.Enabled = enabled;
    }

    private void OnLaunchClick(object? sender, EventArgs e)
    {
        if (!CaptureSelectionState())
            return;
        if (SelectedCredential == null && !CreateNewAccountRequested && SelectedContainer == null && !CreateNewContainerRequested)
            return;
        if (!TryPromptAdHocPassword())
            return;
        DialogResult = DialogResult.OK;
        Close();
    }

    private void RepopulateCredentialList() => _populator.Repopulate();

    private void OnShowAllAccountsChanged(object? sender, EventArgs e) => RepopulateCredentialList();

    private bool TryPromptAdHocPassword()
    {
        if (_credentialListBox.SelectedItem is not CredentialDisplayItem item)
            return true;
        if (item.HasStoredCredential)
            return true;

        var displayName = item.ToString();
        var usernameFallback = SidNameResolver.ResolveUsername(item.Credential.Sid, _sidResolver, _sidNames) ?? displayName;

        using var dlg = new RunAsPasswordDialog(displayName, _windowsAccountService, item.Credential.Sid, usernameFallback);
        if (dlg.ShowDialog(this) != DialogResult.OK)
            return false;

        AdHocPassword = dlg.Password;
        RememberPassword = dlg.RememberPassword;
        return true;
    }
}