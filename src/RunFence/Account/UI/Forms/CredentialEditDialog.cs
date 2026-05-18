using System.ComponentModel;
using RunFence.Account.UI;
using RunFence.Apps.UI;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.UI;

namespace RunFence.Account.UI.Forms;

public partial class CredentialEditDialog : RunFence.UI.Forms.ContextHelpForm
{
    public string Username => _usernameComboBox.Text.Trim();
    public ProtectedString? Password { get; private set; }
    public string? Sid { get; private set; }
    public bool OpenCreateUser { get; private set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ProtectedString? CapturedPassword { get; set; }

    private CredentialEntry? _existing;
    private IReadOnlyList<LocalUserAccount> _localUsers = [];
    private string? _defaultUsername;
    private bool _isEditMode;
    private IReadOnlyDictionary<string, string>? _sidNames;
    private IReadOnlyCollection<string>? _existingSids;
    private readonly IAccountPasswordService _accountService;
    private readonly SidDisplayNameResolver _displayNameResolver;
    private readonly ISidEntryHelper _sidEntryHelper;
    private SecurePasswordBox _passwordSecure = null!;

    public CredentialEditDialog(
        SidDisplayNameResolver displayNameResolver,
        ISidEntryHelper sidEntryHelper,
        IAccountPasswordService accountService)
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
        IReadOnlyList<LocalUserAccount>? localUsers = null,
        string? defaultUsername = null,
        IReadOnlyDictionary<string, string>? sidNames = null,
        IReadOnlyCollection<string>? existingSids = null)
    {
        _existing = existing;
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

        const int margin = 15;
        const int rowGap = 7;
        const int formWidth = 360;

        var yPos = margin;
        _usernameLabel.Location = new Point(margin, yPos);
        yPos += _usernameLabel.PreferredHeight + rowGap;

        _usernameComboBox.Location = new Point(margin, yPos);
        _usernameComboBox.Width = formWidth - margin * 2;

        if (_isEditMode)
        {
            _usernameComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            _usernameComboBox.AutoCompleteMode = AutoCompleteMode.None;
            _usernameComboBox.AutoCompleteSource = AutoCompleteSource.None;

            var displayName = _existing != null ? _displayNameResolver.GetDisplayName(_existing, _sidNames) : "";
            _usernameComboBox.Items.Add(displayName);
            _usernameComboBox.SelectedIndex = 0;
            _usernameComboBox.Enabled = false;

            yPos += _usernameComboBox.PreferredHeight + 4;

            // Show SID text box directly below combo
            _sidTextBox.Location = new Point(margin, yPos);
            _sidTextBox.Width = formWidth - margin * 2;
            _sidTextBox.BackColor = BackColor;
            _sidTextBox.Text = _existing?.Sid ?? "";
            _sidTextBox.Visible = true;
            yPos += _sidTextBox.PreferredHeight + rowGap;
        }
        else
        {
            _usernameComboBox.Text = _defaultUsername ?? "";
            foreach (var user in _localUsers)
                _usernameComboBox.Items.Add(user.Username);
            yPos += _usernameComboBox.PreferredHeight + rowGap;
        }

        _passwordLabel.Location = new Point(margin, yPos);
        yPos += _passwordLabel.PreferredHeight + rowGap;

        _passwordTextBox.Location = new Point(margin, yPos);
        _passwordTextBox.Width = formWidth - margin * 2;
        _passwordSecure = new SecurePasswordBox(_passwordTextBox);
        _passwordSecure.AddEyeToggle();
        yPos += _passwordTextBox.PreferredHeight + rowGap + 5;

        _statusLabel.Location = new Point(margin, yPos);
        _statusLabel.Width = formWidth - margin * 2;
        yPos += _statusLabel.Height + rowGap;

        const int btnWidth = 75;
        const int btnGap = 10;
        const int btnRight = formWidth - btnGap - btnWidth;
        const int btnMiddle = btnRight - btnGap - btnWidth;
        const int btnLeft = btnMiddle - btnGap - btnWidth;

        // Set ClientSize before anchors+positions so anchor distances are computed correctly.
        ClientSize = new Size(formWidth, yPos + 43);

        _okButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        _cancelButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;

        if (_isEditMode)
        {
            _okButton.Location = new Point(btnMiddle, yPos);
            _cancelButton.Location = new Point(btnRight, yPos);
            _passwordTextBox.TextChanged += (_, _) => UpdateOkButtonState();
        }
        else
        {
            _okButton.Text = "Add";
            _createButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            _createButton.Location = new Point(btnLeft, yPos);
            _createButton.Visible = true;
            _okButton.Location = new Point(btnMiddle, yPos);
            _cancelButton.Location = new Point(btnRight, yPos);
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

        AcceptButton = _okButton;
        CancelButton = _cancelButton;

        UpdateOkButtonState();

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

        // Password validation — runas requires a password for non-current accounts.
        // In edit mode, OK is only enabled when password text is non-empty (UpdateOkButtonState),
        // so the text is guaranteed non-empty here.
        if (_passwordSecure.IsEmpty)
        {
            _statusLabel.Text = "Password is required. RunAs will not work without a password.";
            return;
        }

        var pwd = _passwordSecure.GetPassword();

        // Validate credentials via LogonUser (skip for current account)
        var isCurrentAccount = _existing?.IsCurrentAccount == true;
        if (!isCurrentAccount && Sid != null)
        {
            var validationResult = _accountService.ValidatePassword(Sid, pwd, _usernameComboBox.Text.Trim());
            if (validationResult.Status != AccountPasswordStatus.Succeeded)
            {
                pwd.Dispose();
                _statusLabel.Text = validationResult.Error ?? "Credential validation failed.";
                return;
            }
        }

        Password = pwd;

        DialogResult = DialogResult.OK;
        Close();
    }

    private void UpdateOkButtonState()
    {
        if (_isEditMode)
        {
            _okButton.Enabled = !_passwordSecure.IsEmpty;
        }
        else
        {
            var hasUsername = !string.IsNullOrWhiteSpace(_usernameComboBox.Text);
            _okButton.Enabled = hasUsername && !_passwordSecure.IsEmpty;
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
        CapturedPassword = _passwordSecure.GetPassword();
        _passwordSecure.Clear();
        DialogResult = DialogResult.Retry;
        Close();
    }
}
