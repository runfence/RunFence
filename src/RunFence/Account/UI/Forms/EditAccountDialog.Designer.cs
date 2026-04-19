#nullable disable

using System.ComponentModel;

namespace RunFence.Account.UI.Forms;

partial class EditAccountDialog
{
    private IContainer components = null;
    private ToolTip _toolTip;

    private Label _nameLabel;
    private TextBox _usernameTextBox;
    private Label _pwLabel;
    private TextBox _passwordTextBox;
    private Label _confirmLabel;
    private TextBox _confirmPasswordTextBox;
    private Label _groupsLabel;
    private CheckedListBox _groupsListBox;
    private CheckBox _networkLoginCheckBox;
    private CheckBox _logonCheckBox;
    private CheckBox _bgAutorunCheckBox;
    private CheckBox _allowInternetCheckBox;
    private CheckBox _allowLocalhostCheckBox;
    private CheckBox _allowLanCheckBox;
    private CheckBox _ephemeralCheckBox;
    private Label _allowLabel;
    private Label _privilegeLevelLabel;
    private ComboBox _privilegeLevelComboBox;
    private Label _installLabel;
    private CheckedListBox _installListBox;
    private Label _settingsLabel;
    private TextBox _settingsPathTextBox;
    private Button _browseButton;
    private Label _statusLabel;
    private Button _deleteButton;
    private Button _okButton;
    private Button _cancelButton;

    private EditAccountDialog() { InitializeComponent(); }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            components?.Dispose();
            _passwordTextBox.Clear();
            _confirmPasswordTextBox.Clear();
            // NOTE: NewPassword is NOT disposed here — caller owns it
        }
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        _nameLabel = new Label();
        _usernameTextBox = new TextBox();
        _pwLabel = new Label();
        _passwordTextBox = new TextBox();
        _confirmLabel = new Label();
        _confirmPasswordTextBox = new TextBox();
        _groupsLabel = new Label();
        _groupsListBox = new CheckedListBox();
        _networkLoginCheckBox = new CheckBox();
        _logonCheckBox = new CheckBox();
        _bgAutorunCheckBox = new CheckBox();
        _allowInternetCheckBox = new CheckBox();
        _allowLocalhostCheckBox = new CheckBox();
        _allowLanCheckBox = new CheckBox();
        _ephemeralCheckBox = new CheckBox();
        _allowLabel = new Label();
        _toolTip = new ToolTip();
        _privilegeLevelLabel = new Label();
        _privilegeLevelComboBox = new ComboBox();
        _installLabel = new Label();
        _installListBox = new CheckedListBox();
        _settingsLabel = new Label();
        _settingsPathTextBox = new TextBox();
        _browseButton = new Button();
        _statusLabel = new Label();
        _deleteButton = new Button();
        _okButton = new Button();
        _cancelButton = new Button();

        SuspendLayout();

        // ── Left column (x=15, width=270) ──────────────────────────────

        // _nameLabel
        _nameLabel.Text = "Account name:";
        _nameLabel.Location = new Point(15, 15);
        _nameLabel.AutoSize = true;

        // _usernameTextBox
        _usernameTextBox.Location = new Point(15, 37);
        _usernameTextBox.Size = new Size(270, 23);
        _usernameTextBox.MaxLength = 20;

        // _pwLabel
        _pwLabel.Text = "New password (blank to keep existing):";
        _pwLabel.Location = new Point(15, 72);
        _pwLabel.AutoSize = true;

        // _passwordTextBox
        _passwordTextBox.Location = new Point(15, 94);
        _passwordTextBox.Size = new Size(270, 23);
        _passwordTextBox.UseSystemPasswordChar = true;

        // _confirmLabel
        _confirmLabel.Text = "Confirm new password:";
        _confirmLabel.Location = new Point(15, 129);
        _confirmLabel.AutoSize = true;

        // _confirmPasswordTextBox
        _confirmPasswordTextBox.Location = new Point(15, 151);
        _confirmPasswordTextBox.Size = new Size(270, 23);
        _confirmPasswordTextBox.UseSystemPasswordChar = true;

        // _groupsLabel
        _groupsLabel.Text = "Local groups:";
        _groupsLabel.Location = new Point(15, 186);
        _groupsLabel.AutoSize = true;

        // _groupsListBox
        _groupsListBox.Location = new Point(15, 208);
        _groupsListBox.Size = new Size(270, 130);
        _groupsListBox.CheckOnClick = true;

        // _settingsLabel
        _settingsLabel.Text = "Desktop settings file (optional import):";
        _settingsLabel.Location = new Point(15, 355);
        _settingsLabel.AutoSize = true;

        // _settingsPathTextBox
        _settingsPathTextBox.Location = new Point(15, 377);
        _settingsPathTextBox.Size = new Size(195, 23);

        // _browseButton
        _browseButton.Text = "Browse...";
        _browseButton.Location = new Point(218, 376);
        _browseButton.Size = new Size(67, 25);
        _browseButton.FlatStyle = FlatStyle.System;
        _browseButton.Click += OnBrowseSettingsClick;

        // ── Right column (x=305, width=270) ────────────────────────────

        // _allowLabel
        _allowLabel.Text = "Allow:";
        _allowLabel.Location = new Point(305, 15);
        _allowLabel.AutoSize = true;

        // _logonCheckBox
        _logonCheckBox.Text = "Logon";
        _logonCheckBox.Location = new Point(305, 37);
        _logonCheckBox.AutoSize = true;

        // _networkLoginCheckBox
        _networkLoginCheckBox.Text = "Network Login";
        _networkLoginCheckBox.Location = new Point(305, 65);
        _networkLoginCheckBox.AutoSize = true;

        // _bgAutorunCheckBox
        _bgAutorunCheckBox.Text = "Bg Autorun";
        _bgAutorunCheckBox.Location = new Point(305, 93);
        _bgAutorunCheckBox.AutoSize = true;

        // _allowInternetCheckBox
        _allowInternetCheckBox.Text = "Internet";
        _allowInternetCheckBox.Location = new Point(305, 121);
        _allowInternetCheckBox.AutoSize = true;

        // _allowLanCheckBox
        _allowLanCheckBox.Text = "LAN";
        _allowLanCheckBox.Location = new Point(305, 149);
        _allowLanCheckBox.AutoSize = true;

        // _allowLocalhostCheckBox
        _allowLocalhostCheckBox.Text = "Localhost";
        _allowLocalhostCheckBox.Location = new Point(305, 177);
        _allowLocalhostCheckBox.AutoSize = true;

        // _privilegeLevelLabel
        _privilegeLevelLabel.Text = "Default privilege level:";
        _privilegeLevelLabel.Location = new Point(305, 205);
        _privilegeLevelLabel.AutoSize = true;

        // _privilegeLevelComboBox
        _privilegeLevelComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _privilegeLevelComboBox.Location = new Point(305, 227);
        _privilegeLevelComboBox.Size = new Size(200, 23);
        _privilegeLevelComboBox.Items.AddRange(new object[] { "Highest Allowed", "Basic", "Low Integrity" });

        // _ephemeralCheckBox
        _ephemeralCheckBox.Text = "Ephemeral (auto-delete after 24h)";
        _ephemeralCheckBox.Location = new Point(305, 261);
        _ephemeralCheckBox.AutoSize = true;
        _ephemeralCheckBox.Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold);

        // _installLabel
        _installLabel.Text = "Install:";
        _installLabel.Location = new Point(305, 302);
        _installLabel.AutoSize = true;

        // _installListBox
        _installListBox.Location = new Point(305, 324);
        _installListBox.Size = new Size(270, 100);
        _installListBox.CheckOnClick = true;

        // ── Bottom (full width) ─────────────────────────────────────────

        // _statusLabel
        _statusLabel.Location = new Point(15, 442);
        _statusLabel.Size = new Size(560, 20);
        _statusLabel.ForeColor = Color.Red;

        // _deleteButton
        _deleteButton.Text = "Delete Account";
        _deleteButton.Location = new Point(15, 472);
        _deleteButton.Size = new Size(120, 28);
        _deleteButton.FlatStyle = FlatStyle.System;
        _deleteButton.Click += OnDeleteClick;

        // _okButton
        _okButton.Text = "OK";
        _okButton.Location = new Point(415, 472);
        _okButton.Size = new Size(75, 28);
        _okButton.FlatStyle = FlatStyle.System;
        _okButton.Click += OnOkClick;

        // _cancelButton
        _cancelButton.Text = "Cancel";
        _cancelButton.DialogResult = DialogResult.Cancel;
        _cancelButton.Location = new Point(500, 472);
        _cancelButton.Size = new Size(75, 28);
        _cancelButton.FlatStyle = FlatStyle.System;

        // EditAccountDialog
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        Text = "Edit Account";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(590, 512);
        AcceptButton = _okButton;
        CancelButton = _cancelButton;
        Controls.AddRange(new Control[]
        {
            _nameLabel, _usernameTextBox, _pwLabel, _passwordTextBox,
            _confirmLabel, _confirmPasswordTextBox, _groupsLabel, _groupsListBox,
            _settingsLabel, _settingsPathTextBox, _browseButton,
            _logonCheckBox, _networkLoginCheckBox, _bgAutorunCheckBox,
            _allowLabel, _allowInternetCheckBox, _allowLocalhostCheckBox, _allowLanCheckBox,
            _privilegeLevelLabel, _privilegeLevelComboBox, _ephemeralCheckBox,
            _installLabel, _installListBox,
            _statusLabel, _deleteButton, _okButton, _cancelButton
        });

        ResumeLayout(false);
        PerformLayout();
    }
}
