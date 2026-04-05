using System.Security;
using RunFence.Apps.UI;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.UI;

namespace RunFence.Account.UI.Forms;

public partial class CredentialEditDialog : Form
{
    public string Username => _usernameComboBox.Text.Trim();
    public SecureString? Password { get; private set; }
    public string? Sid { get; private set; }
    public bool OpenCreateUser { get; private set; }
    public string? CapturedPasswordText { get; private set; }

    private CredentialEntry? _existing;
    private bool _hasStoredPassword;
    private List<LocalUserAccount> _localUsers = [];
    private string? _defaultUsername;
    private bool _isEditMode;
    private IReadOnlyDictionary<string, string>? _sidNames;
    private IReadOnlyCollection<string>? _existingSids;
    private readonly IWindowsAccountService _accountService;
    private readonly SidDisplayNameResolver _displayNameResolver;
    private readonly ISidEntryHelper _sidEntryHelper;

    public CredentialEditDialog(
        SidDisplayNameResolver displayNameResolver,
        ISidEntryHelper sidEntryHelper,
        IWindowsAccountService accountService)
    {
        _accountService = accountService;
        _displayNameResolver = displayNameResolver;
        _sidEntryHelper = sidEntryHelper;
        InitializeComponent();
        Icon = AppIcons.GetAppIcon();
    }

    /// <summary>
    /// Initializes per-use data for the dialog. Must be called before <see cref="Form.ShowDialog()"/>.
    /// </summary>
    public void Initialize(
        CredentialEntry? existing = null,
        bool hasStoredPassword = false,
        List<LocalUserAccount>? localUsers = null,
        string? defaultUsername = null,
        IReadOnlyDictionary<string, string>? sidNames = null,
        IReadOnlyCollection<string>? existingSids = null)
    {
        _existing = existing;
        _hasStoredPassword = existing != null && hasStoredPassword;
        _localUsers = localUsers ?? [];
        _defaultUsername = defaultUsername;
        _isEditMode = existing != null;
        _sidNames = sidNames;
        _existingSids = existingSids;
        ConfigureLayout();
    }

    private void ConfigureLayout()
    {
        Text = _existing != null ? "Edit Credential" : "Add Credential";

        var yPos = 15;
        // _usernameLabel at (15,15) - set in Designer
        yPos += 22;
        // _usernameComboBox at (15,37) - set in Designer
        yPos += 30; // yPos=67

        if (_isEditMode)
        {
            _usernameComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            _usernameComboBox.AutoCompleteMode = AutoCompleteMode.None;
            _usernameComboBox.AutoCompleteSource = AutoCompleteSource.None;

            var displayName = _existing != null ? _displayNameResolver.GetDisplayName(_existing, _sidNames) : "";
            _usernameComboBox.Items.Add(displayName);
            _usernameComboBox.SelectedIndex = 0;
            _usernameComboBox.Enabled = false;

            // Show SID text box
            _sidTextBox.Location = new Point(15, yPos);
            _sidTextBox.BackColor = BackColor;
            _sidTextBox.Text = _existing?.Sid ?? "";
            _sidTextBox.Visible = true;
            yPos += 25; // yPos=92
        }
        else
        {
            _usernameComboBox.Text = _defaultUsername ?? "";
            foreach (var user in _localUsers)
                _usernameComboBox.Items.Add(user.Username);
        }

        _passwordLabel.Location = new Point(15, yPos);
        yPos += 22;

        _passwordTextBox.Location = new Point(15, yPos);
        PasswordEyeToggle.AddTo(_passwordTextBox);
        yPos += 35;

        _statusLabel.Location = new Point(15, yPos);
        yPos += 25;

        if (_isEditMode)
        {
            _okButton.Location = new Point(185, yPos);
            _cancelButton.Location = new Point(270, yPos);
            _passwordTextBox.TextChanged += (_, _) => UpdateOkButtonState();
        }
        else
        {
            _okButton.Text = "Add";
            _createButton.Location = new Point(100, yPos);
            _createButton.Visible = true;
            _okButton.Location = new Point(185, yPos);
            _cancelButton.Location = new Point(270, yPos);
            _usernameComboBox.TextChanged += (_, _) =>
            {
                UpdateCreateButtonState();
                UpdateOkButtonState();
            };
            _passwordTextBox.TextChanged += (_, _) =>
            {
                UpdateCreateButtonState();
                UpdateOkButtonState();
            };
        }

        ClientSize = new Size(360, yPos + 43);
        AcceptButton = _okButton;
        CancelButton = _cancelButton;

        Shown += (_, _) =>
        {
            if (_isEditMode)
                _passwordTextBox.Focus();
        };
    }

    private void OnOkClick(object? sender, EventArgs e)
    {
        _statusLabel.Text = "";

        if (_isEditMode)
        {
            // Edit mode: only password changes, SID stays the same
            Sid = _existing!.Sid;
        }
        else
        {
            // Add mode: resolve username to SID
            if (string.IsNullOrWhiteSpace(_usernameComboBox.Text))
            {
                _statusLabel.Text = "Username is required.";
                return;
            }

            var enteredName = _usernameComboBox.Text.Trim();

            var resolvedSid = _sidEntryHelper.ResolveOrPrompt(enteredName, _localUsers, this);
            if (resolvedSid == null)
                return;

            if (string.Equals(resolvedSid, SidResolutionHelper.GetCurrentUserSid(), StringComparison.OrdinalIgnoreCase))
            {
                _statusLabel.Text = "Cannot add a credential for the current account.";
                return;
            }

            if (_existingSids != null && _existingSids.Any(s => string.Equals(s, resolvedSid, StringComparison.OrdinalIgnoreCase)))
            {
                _statusLabel.Text = "A credential for this account already exists.";
                return;
            }

            Sid = resolvedSid;
        }

        // Password validation — runas requires a password for non-current accounts
        if (!_hasStoredPassword || _passwordTextBox.Text.Length > 0)
        {
            if (_passwordTextBox.Text.Length == 0)
            {
                _statusLabel.Text = "Password is required. RunAs will not work without a password.";
                return;
            }

            // Validate credentials via LogonUser (skip for current account)
            var isCurrentAccount = _existing?.IsCurrentAccount == true;
            if (!isCurrentAccount && Sid != null)
            {
                var validationResult = _accountService.ValidatePassword(Sid, _passwordTextBox.Text, _usernameComboBox.Text.Trim());
                if (validationResult != null)
                {
                    _statusLabel.Text = validationResult;
                    return;
                }
            }

            Password = new SecureString();
            foreach (char c in _passwordTextBox.Text)
                Password.AppendChar(c);
            Password.MakeReadOnly();
        }

        DialogResult = DialogResult.OK;
        Close();
    }

    private void UpdateOkButtonState()
    {
        if (_isEditMode)
        {
            _okButton.Enabled = _passwordTextBox.Text.Length > 0;
        }
        else
        {
            var hasUsername = !string.IsNullOrWhiteSpace(_usernameComboBox.Text);
            _okButton.Enabled = hasUsername && _passwordTextBox.Text.Length > 0;
        }
    }

    private void UpdateCreateButtonState()
    {
        var name = _usernameComboBox.Text.Trim();
        var nameValid = name.Length is > 0 and <= 20 && name.IndexOfAny(EditAccountDialog.InvalidNameChars) < 0;
        var nameIsNew = !_localUsers.Any(u =>
            string.Equals(u.Username, name, StringComparison.OrdinalIgnoreCase));
        _createButton.Enabled = nameValid && nameIsNew;
    }

    private void OnCreateClick(object? sender, EventArgs e)
    {
        OpenCreateUser = true;
        CapturedPasswordText = _passwordTextBox.Text;
        _passwordTextBox.Clear();
        DialogResult = DialogResult.Retry;
        Close();
    }
}