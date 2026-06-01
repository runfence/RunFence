#region

using RunFence.Account.UI;
using RunFence.Apps.UI;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Persistence;
using RunFence.UI;
using RunFence.UI.Forms;

#endregion

namespace RunFence.Account.UI.Forms;

public partial class EditAccountDialog : RunFence.UI.Forms.ContextHelpForm, IAccountEditResult, IAccountCreationDialog, RunAs.IShowCreateAccountResultDialog
{
    public event Func<Task<bool>>? CreateConfirmRequested;

    public string? NewUsername { get; private set; }
    public ProtectedString? NewPassword { get; private set; }
    public bool IsEphemeral { get; private set; }
    public string? SettingsImportPath { get; private set; }
    public PrivilegeLevel SelectedPrivilegeLevel => _privilegeLevelComboBox.SelectedIndex switch
    {
        0 => PrivilegeLevel.HighestAllowed,
        1 => PrivilegeLevel.HighIntegrity,
        2 => PrivilegeLevel.Basic,
        4 => PrivilegeLevel.LowIntegrity,
        _ => PrivilegeLevel.Isolated,
    };
    public bool DeleteRequested { get; private set; }
    public bool AllowInternet { get; private set; } = true;
    public bool AllowLocalhost { get; private set; } = true;
    public bool AllowLan { get; private set; } = true;
    public bool FirewallSettingsChanged { get; private set; }
    public List<string> Errors { get; } = new();

    // Create mode output properties
    public string? CreatedSid { get; private set; }
    public ProtectedString? CreatedPassword { get; private set; }
    public CreateAccountStatus CreatedAccountStatus { get; private set; }
    public string? CreatedAccountErrorMessage { get; private set; }
    public CreatedAccountRollbackState? CreatedRollbackState { get; private set; }
    public bool UsersGroupUnchecked { get; private set; }
    public bool AdminGroupChecked { get; private set; }
    public IReadOnlyList<InstallablePackage> SelectedInstallPackages { get; private set; } = [];

    // Characters invalid in Windows SAM account names
    public static readonly char[] InvalidNameChars = ['\"', '/', '\\', '[', ']', ':', ';', '|', '=', ',', '+', '*', '?', '<', '>'];

    private readonly ILocalGroupQueryService _groupMembership;
    private readonly IAccountLoginRestrictionService _loginRestriction;
    private readonly IAccountLsaRestrictionService _lsaRestriction;
    private readonly EditAccountDialogCreateHandler _createHandler;
    private readonly EditAccountDialogSaveHandler _saveHandler;
    private readonly IDatabaseProvider _databaseProvider;
    private readonly IOpenFileDialogAdapterFactory _openFileDialogFactory;
    private string _sid = "";
    private string _currentUsername = "";
    private HashSet<string> _currentGroupSids = new(StringComparer.OrdinalIgnoreCase);
    private List<LocalUserAccount> _groups = [];
    private bool? _localOnlyState;
    private bool? _noLogonState;
    private bool? _noBgAutostartState;
    private int _currentHiddenCount;
    private bool _originalAllowInternet;
    private bool _originalAllowLocalhost;
    private bool _originalAllowLan;
    private bool _isCreating;
    private bool _createAttemptOutputOwnershipTransferred;
    private readonly InstallPackageSelector _packageSelector = new();
    private SecurePasswordBox _passwordSecure = null!;
    private SecurePasswordBox _confirmPasswordSecure = null!;

    private static readonly HashSet<string> NeverFilteredGroupNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Hyper-V Administrators",
    };

    public EditAccountDialog(
        ILocalGroupQueryService groupMembership,
        IAccountLoginRestrictionService loginRestriction,
        IAccountLsaRestrictionService lsaRestriction,
        EditAccountDialogCreateHandler createHandler,
        EditAccountDialogSaveHandler saveHandler,
        IDatabaseProvider databaseProvider,
        IOpenFileDialogAdapterFactory openFileDialogFactory)
    {
        _groupMembership = groupMembership;
        _loginRestriction = loginRestriction;
        _lsaRestriction = lsaRestriction;
        _createHandler = createHandler;
        _saveHandler = saveHandler;
        _databaseProvider = databaseProvider;
        _openFileDialogFactory = openFileDialogFactory;
        InitializeComponent();
        Icon = AppIcons.GetAppIcon();
        FormClosed += (_, _) => CleanupOwnedCreateAttemptOutput();
        Disposed += (_, _) => CleanupOwnedCreateAttemptOutput();
    }

    /// <summary>
    /// Initializes the dialog for editing an existing account.
    /// Must be called before <see cref="Form.ShowDialog()"/>.
    /// </summary>
    public void InitializeForEdit(
        string sid,
        string username,
        bool isEphemeral,
        bool isCurrentAccount = false,
        PrivilegeLevel privilegeLevel = PrivilegeLevel.Isolated,
        int currentHiddenCount = 0,
        FirewallAccountSettings? firewallSettings = null,
        IPackageInstallService? packageInstallService = null,
        bool canInstall = true)
    {
        _isCreating = false;
        _sid = sid;
        _currentUsername = username;
        _currentHiddenCount = currentHiddenCount;

        var effectiveFirewall = firewallSettings ?? new FirewallAccountSettings();
        _originalAllowInternet = effectiveFirewall.AllowInternet;
        _originalAllowLocalhost = effectiveFirewall.AllowLocalhost;
        _originalAllowLan = effectiveFirewall.AllowLan;

        _currentGroupSids = _groupMembership.GetGroupsForUser(sid)
            .Select(g => g.Sid)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        _groups = GroupFilterHelper.FilterForEditDialog(
            _groupMembership.GetLocalGroups(), _currentGroupSids, NeverFilteredGroupNames).ToList();

        _localOnlyState = _lsaRestriction.GetLocalOnlyState(sid);
        _noLogonState = _loginRestriction.GetNoLogonState(sid, username);
        _noBgAutostartState = _lsaRestriction.GetNoBgAutostartState(sid);

        _usernameTextBox.Text = username;
        _usernameTextBox.SelectionStart = username.Length;

        _passwordSecure = new SecurePasswordBox(_passwordTextBox);
        _passwordSecure.AddEyeToggle();
        _confirmPasswordSecure = new SecurePasswordBox(_confirmPasswordTextBox);
        _confirmPasswordSecure.AddEyeToggle();

        foreach (var group in _groups)
        {
            _groupsListBox.Items.Add(group.Username);
            _groupsListBox.SetItemChecked(_groupsListBox.Items.Count - 1, _currentGroupSids.Contains(group.Sid));
        }

        _networkLoginCheckBox.CheckState = ToCheckState(Invert(_localOnlyState));
        _logonCheckBox.CheckState = ToCheckState(Invert(_noLogonState));
        _bgAutorunCheckBox.CheckState = ToCheckState(Invert(_noBgAutostartState));
        _allowInternetCheckBox.Checked = effectiveFirewall.AllowInternet;
        _allowLocalhostCheckBox.Checked = effectiveFirewall.AllowLocalhost;
        _allowLanCheckBox.Checked = effectiveFirewall.AllowLan;
        _ephemeralCheckBox.Checked = isEphemeral;
        SetPrivilegeLevel(privilegeLevel);

        if (SidResolutionHelper.IsInteractiveUserSid(sid) && _noLogonState == false)
            _logonCheckBox.Enabled = false;

        if (isCurrentAccount)
        {
            _ephemeralCheckBox.Enabled = false;
            _settingsPathTextBox.Enabled = false;
            _browseButton.Enabled = false;
            _deleteButton.Enabled = false;
        }

        if (!canInstall)
        {
            _settingsPathTextBox.Enabled = false;
            _browseButton.Enabled = false;
        }

        ConfigureInstallList(canInstall, packageInstallService != null ? p => packageInstallService.IsPackageInstalled(p, sid) : null);
        RegisterContextHelp();
    }

    /// <summary>
    /// Initializes the dialog for creating a new Windows account.
    /// Initializes UI defaults from <see cref="AccountCreationDefaults.Create"/>.
    /// Must be called before <see cref="Form.ShowDialog()"/>.
    /// </summary>
    public void InitializeForCreate(
        string? prefillUsername = null,
        ProtectedString? prefillPassword = null,
        int currentHiddenCount = 0)
    {
        _isCreating = true;
        _sid = "";
        _currentUsername = "";
        _currentHiddenCount = currentHiddenCount;
        _currentGroupSids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        _groups = GroupFilterHelper.FilterForCreateDialog(_groupMembership.GetLocalGroups()).ToList();
        ResetCreateAttemptOutput();

        using var defaults = AccountCreationDefaults.Create(_databaseProvider.GetDatabase());

        // Create mode UI adjustments
        Text = "Create Account";
        _pwLabel.Text = "Password:";
        _confirmLabel.Text = "Confirm password:";
        _okButton.Text = "Create";
        _deleteButton.Visible = false;

        _usernameTextBox.Text = prefillUsername ?? defaults.Username;
        _usernameTextBox.SelectionStart = _usernameTextBox.Text.Length;

        _passwordSecure = new SecurePasswordBox(_passwordTextBox);
        _passwordSecure.AddEyeToggle();
        _confirmPasswordSecure = new SecurePasswordBox(_confirmPasswordTextBox);
        _confirmPasswordSecure.AddEyeToggle();

        _passwordSecure.SetFromProtectedString(prefillPassword ?? defaults.Password);
        _confirmPasswordSecure.SetFromProtectedString(prefillPassword ?? defaults.Password);

        var checkedGroupSids = defaults.CheckedGroups.Select(g => g.Sid).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var group in _groups)
        {
            _groupsListBox.Items.Add(group.Username);
            if (checkedGroupSids.Contains(group.Sid))
                _groupsListBox.SetItemChecked(_groupsListBox.Items.Count - 1, true);
        }

        _settingsPathTextBox.Text = defaults.DesktopSettingsPath;
        SetPrivilegeLevel(defaults.PrivilegeLevel);
        _logonCheckBox.CheckState = ToCheckState(defaults.AllowLogon);
        _networkLoginCheckBox.CheckState = ToCheckState(defaults.AllowNetworkLogin);
        // In create mode, bgAutorun cannot be Indeterminate — the value is always known for a new account.
        // Indeterminate is only meaningful when editing an existing account where the state was set externally.
        _bgAutorunCheckBox.ThreeState = false;
        _bgAutorunCheckBox.Checked = defaults.AllowBgAutorun;
        _allowInternetCheckBox.Checked = defaults.AllowInternet;
        _allowLocalhostCheckBox.Checked = defaults.AllowLocalhost;
        _allowLanCheckBox.Checked = defaults.AllowLan;
        _ephemeralCheckBox.Checked = defaults.IsEphemeral;

        ConfigureInstallList(canInstall: true, isPackageInstalled: null);
        RegisterContextHelp();
    }

    private void RegisterContextHelp()
    {
        SetContextHelp(_groupsListBox, ContextHelpTextCatalog.Account_Groups);
        SetContextHelp(_logonCheckBox, ContextHelpTextCatalog.Account_LogonRestriction);
        SetContextHelp(_networkLoginCheckBox, ContextHelpTextCatalog.Account_NetworkLoginRestriction);
        SetContextHelp(_bgAutorunCheckBox, ContextHelpTextCatalog.Account_BgAutorunRestriction);
        SetContextHelp(_privilegeLevelComboBox, ContextHelpTextCatalog.Launch_PrivilegeLevel);
        SetContextHelp(_settingsPathTextBox, ContextHelpTextCatalog.Account_DesktopSettingsImport);
        SetContextHelp(_browseButton, ContextHelpTextCatalog.Account_DesktopSettingsImport);
        SetContextHelp(_ephemeralCheckBox, ContextHelpTextCatalog.EphemeralIdentity);
    }

    public Task<DialogResult> ShowCreateDialogAsync(IWin32Window owner)
        => ShowDialogAsync(owner);

    public void ResetCreateAttemptOutput()
    {
        _createAttemptOutputOwnershipTransferred = false;
        CreatedPassword?.Dispose();
        CreatedSid = null;
        CreatedPassword = null;
        CreatedAccountStatus = CreateAccountStatus.ValidationFailed;
        CreatedAccountErrorMessage = null;
        CreatedRollbackState = null;
        NewUsername = null;
        IsEphemeral = false;
        Errors.Clear();
        SettingsImportPath = null;
        FirewallSettingsChanged = false;
        AllowInternet = true;
        AllowLocalhost = true;
        AllowLan = true;
        UsersGroupUnchecked = false;
        AdminGroupChecked = false;
        SelectedInstallPackages = [];
    }

    private void ConfigureInstallList(bool canInstall, Func<InstallablePackage, bool>? isPackageInstalled)
    {
        _packageSelector.Configure(_installListBox, canInstall, isPackageInstalled);
        if (canInstall)
            _installListBox.ItemCheck += OnInstallItemCheck;
    }

    private void OnInstallItemCheck(object? sender, ItemCheckEventArgs e)
    {
        var overrideState = _packageSelector.ValidateItemCheck(_installListBox, e.Index, e.NewValue);
        if (overrideState.HasValue)
            e.NewValue = overrideState.Value;
    }

    private IReadOnlyList<InstallablePackage> CollectSelectedInstallPackages()
        => _packageSelector.GetSelectedPackages(_installListBox);

    private void OnBrowseSettingsClick(object? sender, EventArgs e)
    {
        using var dlgAdapter = _openFileDialogFactory.Create();
        var dlg = dlgAdapter.Dialog;
        dlg.Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*";
        dlg.Title = "Select Desktop Settings File";
        if (dlgAdapter.ShowDialog(owner: null) == DialogResult.OK)
            _settingsPathTextBox.Text = dlg.FileName;
    }

    /// <summary>
    /// Validates a Windows SAM account name. Returns null if valid, or an error message if invalid.
    /// </summary>
    private static string? ValidateUsername(string name)
    {
        if (name.Length is 0 or > 20)
            return "Account name must be 1\u201320 characters.";
        if (name.IndexOfAny(InvalidNameChars) >= 0)
            return "Account name contains invalid characters.";
        if (name.EndsWith('.'))
            return "Account name cannot end with a period.";
        return null;
    }

    /// <remarks>OS calls (LSA, SAM) are thread-safe; UI state is captured before Task.Run.</remarks>
    private async void OnOkClick(object? sender, EventArgs e)
    {
        if (_isCreating)
        {
            await OnCreateClick();
            return;
        }

        _okButton.Enabled = false;
        _cancelButton.Enabled = false;
        _statusLabel.Text = "";
        Errors.Clear();

        // Validate username
        var name = _usernameTextBox.Text.Trim();
        var usernameError = ValidateUsername(name);
        if (usernameError != null)
        {
            _statusLabel.Text = usernameError;
            _okButton.Enabled = true;
            _cancelButton.Enabled = true;
            return;
        }

        // Validate password confirmation
        if (_passwordSecure.GetPasswordLength() > 0 && !_passwordSecure.PasswordsMatch(_confirmPasswordSecure))
        {
            _statusLabel.Text = "Passwords do not match.";
            _okButton.Enabled = true;
            _cancelButton.Enabled = true;
            return;
        }

        // Group diff: only among groups shown in this dialog
        var groupsToAdd = new List<string>();
        var groupsToRemove = new List<string>();
        for (int i = 0; i < _groupsListBox.Items.Count; i++)
        {
            var groupSid = _groups[i].Sid;
            var wasChecked = _currentGroupSids.Contains(groupSid);
            var isChecked = _groupsListBox.GetItemChecked(i);
            if (isChecked && !wasChecked)
                groupsToAdd.Add(groupSid);
            else if (!isChecked && wasChecked)
                groupsToRemove.Add(groupSid);
        }

        var adminGroup = _groups.FirstOrDefault(g =>
            string.Equals(g.Sid, GroupFilterHelper.AdministratorsSid, StringComparison.OrdinalIgnoreCase));

        // With three-state display (Indeterminate for partial conditions), comparing CheckState
        // prevents spurious writes when the user didn't touch the checkbox.
        // Null = unchanged; non-null = changed, apply new value.
        static bool? ChangedValue(CheckState current, CheckState original) =>
            current != original ? current == CheckState.Checked : null;

        // Capture all UI state before background execution
        var request = new EditAccountDialogSaveHandler.SaveAccountRequest(
            Sid: _sid,
            CurrentUsername: _currentUsername,
            NewName: name,
            GroupsToAdd: groupsToAdd,
            GroupsToRemove: groupsToRemove,
            AdminGroupSid: adminGroup?.Sid,
            NewNetworkLogin: ChangedValue(_networkLoginCheckBox.CheckState, ToCheckState(Invert(_localOnlyState))),
            NewLogon: ChangedValue(_logonCheckBox.CheckState, ToCheckState(Invert(_noLogonState))),
            NewBgAutorun: ChangedValue(_bgAutorunCheckBox.CheckState, ToCheckState(Invert(_noBgAutostartState))),
            CurrentHiddenCount: _currentHiddenCount,
            NoLogonState: _noLogonState);
        var settingsPath = _settingsPathTextBox.Text.Trim();
        var allowInternet = _allowInternetCheckBox.Checked;
        var allowLocalhost = _allowLocalhostCheckBox.Checked;
        var allowLan = _allowLanCheckBox.Checked;
        var isEphemeral = _ephemeralCheckBox.Checked;
        var newPassword = _passwordSecure.IsEmpty ? null : _passwordSecure.GetPassword();
        var installPackages = CollectSelectedInstallPackages();

        // Execute slow OS operations (group membership queries, renames, restrictions) on background thread.
        var saveResult = await Task.Run(() => _saveHandler.Execute(request));

        if (IsDisposed)
        {
            newPassword?.Dispose();
            return;
        }

        if (saveResult.Status == EditAccountDialogSaveHandler.SaveAccountStatus.ValidationFailed || saveResult.ValidationError != null)
        {
            newPassword?.Dispose();
            _statusLabel.Text = saveResult.ValidationError;
            _okButton.Enabled = true;
            _cancelButton.Enabled = true;
            return;
        }

        Errors.AddRange(saveResult.Errors);

        // Desktop settings import path (actual import is done by the caller after password change)
        if (settingsPath.Length > 0 && File.Exists(settingsPath))
            SettingsImportPath = settingsPath;

        // Firewall settings: collect and apply if changed
        AllowInternet = allowInternet;
        AllowLocalhost = allowLocalhost;
        AllowLan = allowLan;
        FirewallSettingsChanged = AllowInternet != _originalAllowInternet
                                  || AllowLocalhost != _originalAllowLocalhost
                                  || AllowLan != _originalAllowLan;

        NewUsername = saveResult.NewUsername;
        IsEphemeral = isEphemeral;

        if (newPassword != null)
            NewPassword = newPassword;

        SelectedInstallPackages = installPackages;

        DialogResult = DialogResult.OK;
        Close();
    }

    /// <remarks>OS calls (CreateLocalUser, AddUserToGroups, LSA) are thread-safe; UI state is captured before Task.Run.</remarks>
    private async Task OnCreateClick()
    {
        ResetCreateAttemptOutput();
        _okButton.Enabled = false;
        _cancelButton.Enabled = false;
        _statusLabel.Text = "";

        // Validate username
        var name = _usernameTextBox.Text.Trim();
        var usernameError = ValidateUsername(name);
        if (usernameError != null)
        {
            _statusLabel.Text = usernameError;
            _okButton.Enabled = true;
            _cancelButton.Enabled = true;
            return;
        }

        if (_passwordSecure.IsEmpty)
        {
            _statusLabel.Text = "Password is required.";
            _okButton.Enabled = true;
            _cancelButton.Enabled = true;
            return;
        }

        // Windows auto-adds new users to the Users group; remove it post-creation if unchecked
        var uncheckedDefaultGroups = _groups
            .Select((g, i) => (Group: g, Index: i))
            .Where(x => string.Equals(x.Group.Sid, GroupFilterHelper.UsersSid, StringComparison.OrdinalIgnoreCase)
                        && !_groupsListBox.GetItemChecked(x.Index))
            .Select(x => (x.Group.Sid, x.Group.Username))
            .ToList();

        // Collect explicitly-checked non-default groups to add.
        // Users is excluded: Windows auto-adds to it on creation; explicit add would throw.
        var checkedGroups = Enumerable.Range(0, _groupsListBox.Items.Count)
            .Where(i => _groupsListBox.GetItemChecked(i)
                        && !string.Equals(_groups[i].Sid, GroupFilterHelper.UsersSid, StringComparison.OrdinalIgnoreCase))
            .Select(i => (_groups[i].Sid, _groups[i].Username))
            .ToList();

        var request = new EditAccountDialogCreateHandler.CreateAccountRequest(
            Username: _usernameTextBox.Text.Trim(),
            Password: _passwordSecure.GetPassword(),
            ConfirmPassword: _confirmPasswordSecure.GetPassword(),
            IsEphemeral: _ephemeralCheckBox.Checked,
            CheckedGroups: checkedGroups,
            UncheckedGroups: uncheckedDefaultGroups,
            AllowLogon: _logonCheckBox.Checked,
            AllowNetworkLogin: _networkLoginCheckBox.Checked,
            AllowBgAutorun: _bgAutorunCheckBox.Checked,
            CurrentHiddenCount: _currentHiddenCount);

        // Capture remaining UI state before background execution
        var settingsPath = _settingsPathTextBox.Text.Trim();
        var allowInternet = _allowInternetCheckBox.Checked;
        var allowLocalhost = _allowLocalhostCheckBox.Checked;
        var allowLan = _allowLanCheckBox.Checked;
        var installPackages = CollectSelectedInstallPackages();

        // Execute slow OS operations (CreateLocalUser, group membership, LSA) on background thread.
        var result = await Task.Run(() => _createHandler.Execute(request));
        request.Password.Dispose();
        request.ConfirmPassword.Dispose();

        if (IsDisposed)
            return;

        if (result.Status is CreateAccountStatus.ValidationFailed or CreateAccountStatus.WindowsAccountCreationFailed)
        {
            _statusLabel.Text = result.ErrorMessage ?? _createHandler.LastValidationError;
            _okButton.Enabled = true;
            _cancelButton.Enabled = true;
            return;
        }

        CreatedAccountStatus = result.Status;
        CreatedAccountErrorMessage = result.ErrorMessage;
        CreatedRollbackState = result.RollbackState;

        if (result.Status == CreateAccountStatus.CleanupStateSaveFailed)
        {
            CreatedSid = result.Sid;
            CreatedPassword = null;
            NewUsername = result.Username;
            IsEphemeral = result.IsEphemeral;
            var shouldCloseAfterWarning = await RequestCreateConfirmAsync();
            if (!shouldCloseAfterWarning)
            {
                _okButton.Enabled = true;
                _cancelButton.Enabled = true;
                return;
            }

            _createAttemptOutputOwnershipTransferred = true;
            DialogResult = DialogResult.OK;
            Close();
            return;
        }

        // Map result to dialog output properties
        CreatedSid = result.Sid;
        CreatedPassword = result.Password;
        NewUsername = result.Username;
        IsEphemeral = result.IsEphemeral;
        Errors.AddRange(result.Errors);
        UsersGroupUnchecked = uncheckedDefaultGroups.Any(g =>
            string.Equals(g.Sid, GroupFilterHelper.UsersSid, StringComparison.OrdinalIgnoreCase));
        AdminGroupChecked = checkedGroups.Any(g =>
            string.Equals(g.Sid, GroupFilterHelper.AdministratorsSid, StringComparison.OrdinalIgnoreCase));

        // Desktop settings import path (actual import is done by the caller)
        if (settingsPath.Length > 0 && File.Exists(settingsPath))
            SettingsImportPath = settingsPath;

        // Firewall settings
        AllowInternet = allowInternet;
        AllowLocalhost = allowLocalhost;
        AllowLan = allowLan;
        FirewallSettingsChanged = !AllowInternet || !AllowLocalhost || !AllowLan;

        SelectedInstallPackages = installPackages;

        var shouldClose = await RequestCreateConfirmAsync();
        if (!shouldClose)
        {
            _okButton.Enabled = true;
            _cancelButton.Enabled = true;
            return;
        }

        _createAttemptOutputOwnershipTransferred = true;
        DialogResult = DialogResult.OK;
        Close();
    }

    private void OnDeleteClick(object? sender, EventArgs e)
    {
        DeleteRequested = true;
        DialogResult = DialogResult.Cancel;
        Close();
    }

    private void SetPrivilegeLevel(PrivilegeLevel mode)
    {
        _privilegeLevelComboBox.SelectedIndex = mode switch
        {
            PrivilegeLevel.HighestAllowed => 0,
            PrivilegeLevel.HighIntegrity => 1,
            PrivilegeLevel.Basic => 2,
            PrivilegeLevel.LowIntegrity => 4,
            _ => 3,
        };
    }

    private static bool? Invert(bool? v) => !v;

    private static CheckState ToCheckState(bool? state) => state switch
    {
        true => CheckState.Checked,
        false => CheckState.Unchecked,
        null => CheckState.Indeterminate
    };

    private async Task<bool> RequestCreateConfirmAsync()
    {
        var createConfirmRequested = CreateConfirmRequested;
        if (createConfirmRequested == null)
            return true;

        foreach (Func<Task<bool>> handler in createConfirmRequested.GetInvocationList())
        {
            if (!await handler())
                return false;
        }

        return true;
    }

    private void CleanupOwnedCreateAttemptOutput()
    {
        if (!_isCreating)
            return;

        if (_createAttemptOutputOwnershipTransferred)
            return;

        ResetCreateAttemptOutput();
    }
}
