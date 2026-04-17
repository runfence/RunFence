#region

using System.Security;
using RunFence.Account.UI;
using RunFence.Apps.UI;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Launch;
using RunFence.Persistence;
using RunFence.UI;

#endregion

namespace RunFence.Account.UI.Forms;

public partial class EditAccountDialog : Form
{
    public string? NewUsername { get; private set; }
    public SecureString? NewPassword { get; private set; }
    public string? NewPasswordText { get; private set; }
    public bool IsEphemeral { get; private set; }
    public string? SettingsImportPath { get; private set; }
    public PrivilegeLevel SelectedPrivilegeLevel => _privilegeLevelComboBox.SelectedIndex switch
    {
        0 => PrivilegeLevel.HighestAllowed,
        2 => PrivilegeLevel.LowIntegrity,
        _ => PrivilegeLevel.Basic,
    };
    public bool DeleteRequested { get; private set; }
    public bool AllowInternet { get; private set; } = true;
    public bool AllowLocalhost { get; private set; } = true;
    public bool AllowLan { get; private set; } = true;
    public bool FirewallSettingsChanged { get; private set; }
    public List<string> Errors { get; } = new();

    // Create mode output properties
    public string? CreatedSid { get; private set; }
    public SecureString? CreatedPassword { get; private set; }
    public bool UsersGroupUnchecked { get; private set; }
    public IReadOnlyList<InstallablePackage> SelectedInstallPackages { get; private set; } = [];

    // Characters invalid in Windows SAM account names
    public static readonly char[] InvalidNameChars = ['\"', '/', '\\', '[', ']', ':', ';', '|', '=', ',', '+', '*', '?', '<', '>'];

    private readonly ILocalGroupMembershipService _groupMembership;
    private readonly IAccountLoginRestrictionService _loginRestriction;
    private readonly IAccountLsaRestrictionService _lsaRestriction;
    private readonly EditAccountDialogCreateHandler _createHandler;
    private readonly EditAccountDialogSaveHandler _saveHandler;
    private readonly IDatabaseProvider _databaseProvider;
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
    private readonly InstallPackageSelector _packageSelector = new();

    private static readonly HashSet<string> NeverFilteredGroupNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Hyper-V Administrators",
    };

    public EditAccountDialog(
        ILocalGroupMembershipService groupMembership,
        IAccountLoginRestrictionService loginRestriction,
        IAccountLsaRestrictionService lsaRestriction,
        EditAccountDialogCreateHandler createHandler,
        EditAccountDialogSaveHandler saveHandler,
        IDatabaseProvider databaseProvider)
    {
        _groupMembership = groupMembership;
        _loginRestriction = loginRestriction;
        _lsaRestriction = lsaRestriction;
        _createHandler = createHandler;
        _saveHandler = saveHandler;
        _databaseProvider = databaseProvider;
        InitializeComponent();
        Icon = AppIcons.GetAppIcon();
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
        PrivilegeLevel privilegeLevel = PrivilegeLevel.Basic,
        int currentHiddenCount = 0,
        FirewallAccountSettings? firewallSettings = null,
        PackageInstallService? packageInstallService = null,
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

        PasswordEyeToggle.AddTo(_passwordTextBox);
        PasswordEyeToggle.AddTo(_confirmPasswordTextBox);

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
    }

    /// <summary>
    /// Initializes the dialog for creating a new Windows account.
    /// Initializes UI defaults from <see cref="AccountCreationDefaults.Create"/>.
    /// Must be called before <see cref="Form.ShowDialog()"/>.
    /// </summary>
    public void InitializeForCreate(
        string? prefillUsername = null,
        string? prefillPassword = null,
        int currentHiddenCount = 0)
    {
        _isCreating = true;
        _sid = "";
        _currentUsername = "";
        _currentHiddenCount = currentHiddenCount;
        _currentGroupSids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        _groups = GroupFilterHelper.FilterForCreateDialog(_groupMembership.GetLocalGroups()).ToList();

        var defaults = AccountCreationDefaults.Create(_databaseProvider.GetDatabase(), _groupMembership);

        // Create mode UI adjustments
        Text = "Create Account";
        _pwLabel.Text = "Password:";
        _confirmLabel.Text = "Confirm password:";
        _okButton.Text = "Create";
        _deleteButton.Visible = false;

        _usernameTextBox.Text = prefillUsername ?? defaults.Username;
        _usernameTextBox.SelectionStart = _usernameTextBox.Text.Length;

        var password = prefillPassword ?? defaults.Password;
        _passwordTextBox.Text = password;
        _confirmPasswordTextBox.Text = password;

        PasswordEyeToggle.AddTo(_passwordTextBox);
        PasswordEyeToggle.AddTo(_confirmPasswordTextBox);

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
        using var dlg = new OpenFileDialog();
        dlg.Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*";
        dlg.Title = "Select Desktop Settings File";
        FileDialogHelper.AddInteractiveUserCustomPlaces(dlg);
        if (dlg.ShowDialog() == DialogResult.OK)
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
            OnCreateClick();
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
        if (_passwordTextBox.Text.Length > 0 && _passwordTextBox.Text != _confirmPasswordTextBox.Text)
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
        var passwordText = _passwordTextBox.Text;
        var installPackages = CollectSelectedInstallPackages();

        // Execute slow OS operations (group membership queries, renames, restrictions) on background thread.
        var saveResult = await Task.Run(() => _saveHandler.Execute(request));

        if (IsDisposed)
            return;

        if (saveResult.ValidationError != null)
        {
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

        if (passwordText.Length > 0)
        {
            NewPassword = new SecureString();
            foreach (char c in passwordText)
                NewPassword.AppendChar(c);
            NewPassword.MakeReadOnly();
            NewPasswordText = passwordText;
        }

        SelectedInstallPackages = installPackages;

        DialogResult = DialogResult.OK;
        Close();
    }

    private void OnCreateClick()
    {
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

        if (_passwordTextBox.Text.Length == 0)
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
            PasswordText: _passwordTextBox.Text,
            ConfirmPasswordText: _confirmPasswordTextBox.Text,
            IsEphemeral: _ephemeralCheckBox.Checked,
            CheckedGroups: checkedGroups,
            UncheckedGroups: uncheckedDefaultGroups,
            AllowLogon: _logonCheckBox.Checked,
            AllowNetworkLogin: _networkLoginCheckBox.Checked,
            AllowBgAutorun: _bgAutorunCheckBox.Checked,
            CurrentHiddenCount: _currentHiddenCount);

        var result = _createHandler.Execute(request);
        if (result == null)
        {
            _statusLabel.Text = _createHandler.LastValidationError;
            _okButton.Enabled = true;
            _cancelButton.Enabled = true;
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

        // Desktop settings import path (actual import is done by the caller)
        var settingsPath = _settingsPathTextBox.Text.Trim();
        if (settingsPath.Length > 0 && File.Exists(settingsPath))
            SettingsImportPath = settingsPath;

        // Firewall settings
        AllowInternet = _allowInternetCheckBox.Checked;
        AllowLocalhost = _allowLocalhostCheckBox.Checked;
        AllowLan = _allowLanCheckBox.Checked;
        FirewallSettingsChanged = !AllowInternet || !AllowLocalhost || !AllowLan;

        SelectedInstallPackages = CollectSelectedInstallPackages();

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
            PrivilegeLevel.LowIntegrity => 2,
            _ => 1,
        };
        _toolTip.SetToolTip(_privilegeLevelComboBox, LaunchUiConstants.PrivilegeLevelTooltip);
    }

    private static bool? Invert(bool? v) => !v;

    private static CheckState ToCheckState(bool? state) => state switch
    {
        true => CheckState.Checked,
        false => CheckState.Unchecked,
        null => CheckState.Indeterminate
    };
}