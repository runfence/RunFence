using RunFence.Account;
using RunFence.Account.UI;
using RunFence.Account.UI.AppContainer;
using RunFence.Acl.Permissions;
using RunFence.Acl.UI;
using RunFence.Apps.Shortcuts;
using RunFence.Apps.UI;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.UI;
using RunFence.UI.Forms;

namespace RunFence.RunAs.UI.Forms;

/// <remarks>Methods above threshold: 22 methods, 435 lines: already has Populator and Renderer extracted. Remaining methods are event handlers + <c>CaptureResult</c>. Extracting layout helpers creates 1:1 coupling with the dialog's controls. Extracting state management duplicates control references. Reviewed 2026-04-09.</remarks>
public partial class RunAsDialog : RunFence.UI.Forms.ContextHelpForm
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
    private ProtectedString? AdHocPassword { get; set; }
    private bool RememberPassword { get; set; }

    private string? _initialAccountSid;
    private string? _lastUsedAccountSid;
    private string? _lastUsedContainerName;
    private string? _currentUserSid;
    private IReadOnlyDictionary<string, string>? _sidNames;
    private IReadOnlyDictionary<string, PrivilegeLevel>? _accountPrivilegeLevels;
    private readonly ISidResolver _sidResolver;
    private readonly RunAsCredentialListPopulator _populator;
    private readonly RunAsCredentialListRenderer _renderer;
    private readonly IAclPermissionService _aclPermission;
    private readonly RunAsAccountOptionCatalog _optionCatalog;
    private readonly RunAsSelectionPolicy _selectionPolicy;
    private readonly IRunAsAdHocPasswordPromptService _adHocPasswordPromptService;
    private readonly IRunAsAncestorPermissionPrompter _permissionPrompter;
    private readonly ToolTip _toolTip = new();

    public RunAsDialog(
        ISidResolver sidResolver,
        IAclPermissionService aclPermission,
        RunAsCredentialListPopulator populator,
        RunAsCredentialListRenderer renderer,
        RunAsAccountOptionCatalog optionCatalog,
        RunAsSelectionPolicy selectionPolicy,
        IRunAsAdHocPasswordPromptService adHocPasswordPromptService,
        IRunAsAncestorPermissionPrompter permissionPrompter)
    {
        _sidResolver = sidResolver;
        _aclPermission = aclPermission;
        _populator = populator;
        _renderer = renderer;
        _optionCatalog = optionCatalog;
        _selectionPolicy = selectionPolicy;
        _adHocPasswordPromptService = adHocPasswordPromptService;
        _permissionPrompter = permissionPrompter;
        InitializeComponent();
        Icon = AppIcons.GetAppIcon();
    }

    private static readonly PrivilegeLevel[] PrivilegeLevelMapping = [PrivilegeLevel.HighestAllowed, PrivilegeLevel.HighIntegrity, PrivilegeLevel.Basic, PrivilegeLevel.Isolated, PrivilegeLevel.LowIntegrity];

    /// <summary>
    /// Initializes per-use dialog data. Must be called before <see cref="Form.ShowDialog()"/>.
    /// </summary>
    public void Initialize(RunAsDialogOptions options)
    {
        _filePath = options.FilePath;
        _arguments = options.Arguments;
        _credentials = options.Credentials;
        _existingApps = options.ExistingApps;
        _initialAccountSid = options.InitialAccountSid;
        _lastUsedAccountSid = options.LastUsedAccountSid;
        _lastUsedContainerName = options.LastUsedContainerName;
        _currentUserSid = options.CurrentUserSid;
        _sidNames = options.SidNames;
        _shortcutContext = options.ShortcutContext;
        _appContainers = options.AppContainers;
        _accountPrivilegeLevels = options.AccountPrivilegeLevels;
        _dialogState = new RunAsDialogState(_filePath, options.SidsNeedingPermission, _aclPermission, _permissionPrompter);

        _populator.Initialize(
            _credentialListBox, _credentials, _sidNames, _showAllAccountsCheckBox,
            _currentUserSid, _initialAccountSid, _appContainers, showSystemAccount: options.ShowSystemInRunAs);
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
        y += _pathHeaderLabel.PreferredSize.Height + 7;

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
            y += _shortcutLabel.PreferredSize.Height + 5;
        }

        if (!string.IsNullOrEmpty(_arguments))
        {
            _argsLabel.Location = new Point(15, y);
            _argsLabel.Visible = true;
            y += _argsLabel.PreferredSize.Height + 5;

            _argsTextBox.Text = _arguments;
            _argsTextBox.Location = new Point(15, y);
            _argsTextBox.Visible = true;
            y += _argsTextBox.Height + 7;
        }

        _credLabel.Location = new Point(15, y);
        y += _credLabel.PreferredSize.Height + 5;

        _showAllAccountsCheckBox.Location = new Point(15, y);
        _showAllAccountsCheckBox.Visible = true;
        y += _showAllAccountsCheckBox.PreferredSize.Height + 5;

        _credentialListBox.Location = new Point(15, y);
        RepopulateCredentialList();
        y += _credentialListBox.Height + 7;

        if (_shortcutContext is { IsAlreadyManaged: false })
        {
            _updateShortcutCheckBox.Location = new Point(15, y);
            _updateShortcutCheckBox.Visible = true;
            y += _updateShortcutCheckBox.PreferredSize.Height + 8;
        }

        var clientWidth = ClientSize.Width;
        _privilegeLevelLabel.Location = new Point(15, y + 5);
        _privilegeLevelComboBox.Location = new Point(clientWidth - 15 - _privilegeLevelComboBox.Width, y);
        y += _privilegeLevelComboBox.Height + 14;

        var cancelLeft = clientWidth - 15 - _cancelButton.Width;
        var addAppLeft = cancelLeft - 5 - _addAppButton.Width;
        var launchLeft = addAppLeft - 5 - _launchButton.Width;

        if (_shortcutContext is { IsAlreadyManaged: true, ManagedApp: not null })
        {
            _revertButton.Location = new Point(15, y);
            _revertButton.Visible = true;
        }

        _launchButton.Location = new Point(launchLeft, y);
        _addAppButton.Location = new Point(addAppLeft, y);
        _cancelButton.Location = new Point(cancelLeft, y);

        ClientSize = ClientSize with { Height = y + _launchButton.Height + 17 };

        // Set anchors after ClientSize is finalized so anchor distances are computed correctly.
        _credentialListBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        _revertButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        _launchButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        _addAppButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        _cancelButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;

        ApplyInitialSelection();

        RegisterContextHelp();
    }

    private void RegisterContextHelp()
    {
        SetContextHelp(_argsTextBox, ContextHelpTextCatalog.Launch_Arguments);
        SetContextHelp(_privilegeLevelComboBox, ContextHelpTextCatalog.Launch_PrivilegeLevel);
    }

    private void ApplyInitialSelection()
    {
        var options = BuildAccountOptions();
        if (options.Count == 0)
        {
            ApplySelectionState(null);
            return;
        }

        var selection = _selectionPolicy.ResolveSelection(
            options.Select(o => o.Option).ToList(),
            _initialAccountSid,
            _lastUsedAccountSid,
            _lastUsedContainerName,
            _currentUserSid,
            _shortcutContext,
            app: null);
        _credentialListBox.SelectedIndex = selection.SelectedIndex >= 0
            ? options[selection.SelectedIndex].ListIndex
            : -1;
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
        var selectedListItem = GetSelectedListItem();
        if (selectedListItem?.IsSeparator == true)
        {
            var current = _credentialListBox.SelectedIndex;
            var navigatingDown = current > _lastCredentialListIndex;
            if (navigatingDown)
            {
                var next = current + 1;
                if (next < _credentialListBox.Items.Count)
                    _credentialListBox.SelectedIndex = next;
            }
            else
            {
                var previous = current - 1;
                if (previous >= 0)
                    _credentialListBox.SelectedIndex = previous;
            }
            return;
        }

        _lastCredentialListIndex = _credentialListBox.SelectedIndex;

        ApplySelectionState(GetSelectedOption());
    }

    private void ApplySelectionState(RunAsAccountOptionListEntry? selection)
    {
        if (selection == null)
        {
            _currentExistingApp = null;
            SetPrivilegeLevel(PrivilegeLevel.Isolated, enabled: false);
            _addAppButton.Enabled = false;
            UpdateLaunchButtonState();
            return;
        }

        var result = _selectionPolicy.ResolveSelection(selection.Option);
        _currentExistingApp = result.ExistingAppForSelection;
        SetPrivilegeLevel(result.PrivilegeLevel, result.PrivilegeSelectionEnabled);
        if (selection.Option is not CreateContainerRunAsOption)
            _addAppButton.Text = result.AddAppButtonText;
        _addAppButton.Enabled = result.AddAppButtonEnabled;
        UpdateLaunchButtonState();
    }

    private void UpdateLaunchButtonState()
    {
        var hasCredential = GetSelectedOption() != null;
        var shortcutBlocked = _updateShortcutCheckBox.Visible && _updateShortcutCheckBox.Checked && _currentExistingApp == null;
        _launchButton.Enabled = hasCredential && !shortcutBlocked;
        _toolTip.SetToolTip(_launchButton, shortcutBlocked
            ? "Uncheck 'Update this shortcut' or add app entry first"
            : null);
    }

    private bool CaptureSelectionState()
    {
        bool ok = _dialogState.ResolveSelectionState(
            GetSelectedOption()?.Option,
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

    private void RepopulateCredentialList()
    {
        _populator.Repopulate();
        ApplySelectionState(GetSelectedOption());
    }

    private void OnShowAllAccountsChanged(object? sender, EventArgs e) => RepopulateCredentialList();

    private bool TryPromptAdHocPassword()
    {
        var item = GetSelectedListItem()?.DisplayItem as CredentialDisplayItem;
        if (item == null)
            return true;
        if (item.HasStoredCredential)
            return true;
        if (SidResolutionHelper.CanLaunchWithoutPassword(item.Credential.Sid))
            return true;

        var displayName = item.ToString();
        var usernameFallback = SidNameResolver.ResolveUsername(item.Credential.Sid, _sidResolver, _sidNames) ?? displayName;
        var result = _adHocPasswordPromptService.Prompt(
            this,
            item.Credential.Sid,
            usernameFallback,
            displayName,
            allowRememberPassword: true);
        if (!result.Accepted)
            return false;

        AdHocPassword = result.Password;
        RememberPassword = result.RememberPassword;
        return true;
    }

    private IReadOnlyList<RunAsAccountOptionListEntry> BuildAccountOptions()
        => _optionCatalog.Build(
            _credentialListBox.Items
                .Cast<object>()
                .OfType<RunAsAccountListItem>()
                .Select(item => item.OptionSource)
                .OfType<RunAsAccountOptionSource>()
                .ToList(),
            _existingApps,
            _filePath,
            _currentUserSid,
            _accountPrivilegeLevels);

    private RunAsAccountOptionListEntry? GetSelectedOption()
        => _optionCatalog.GetSelected(
            GetSelectedListItem()?.OptionSource,
            _credentialListBox.SelectedIndex,
            _existingApps,
            _filePath,
            _currentUserSid,
            _accountPrivilegeLevels);

    private RunAsAccountListItem? GetSelectedListItem() => _credentialListBox.SelectedItem as RunAsAccountListItem;
}
