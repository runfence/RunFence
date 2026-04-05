#nullable disable

using System.ComponentModel;

namespace RunFence.Account.UI.Forms;

partial class CredentialEditDialog
{
    private IContainer components = null;

    private Label _usernameLabel;
    private ComboBox _usernameComboBox;
    private TextBox _sidTextBox;
    private Label _passwordLabel;
    private TextBox _passwordTextBox;
    private Label _statusLabel;
    private Button _createButton;
    private Button _okButton;
    private Button _cancelButton;

    private CredentialEditDialog() { InitializeComponent(); }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            components?.Dispose();
            _passwordTextBox.Clear();
            CapturedPasswordText = null;
        }
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        _usernameLabel = new Label();
        _usernameComboBox = new ComboBox();
        _sidTextBox = new TextBox();
        _passwordLabel = new Label();
        _passwordTextBox = new TextBox();
        _statusLabel = new Label();
        _createButton = new Button();
        _okButton = new Button();
        _cancelButton = new Button();

        SuspendLayout();

        // _usernameLabel
        _usernameLabel.Text = "Username:";
        _usernameLabel.Location = new Point(15, 15);
        _usernameLabel.AutoSize = true;

        // _usernameComboBox (add mode defaults; overridden in code for edit mode)
        _usernameComboBox.Location = new Point(15, 37);
        _usernameComboBox.Size = new Size(330, 23);
        _usernameComboBox.DropDownStyle = ComboBoxStyle.DropDown;
        _usernameComboBox.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
        _usernameComboBox.AutoCompleteSource = AutoCompleteSource.ListItems;

        // _sidTextBox (edit mode only, hidden by default, position set in code)
        _sidTextBox.Size = new Size(330, 23);
        _sidTextBox.ReadOnly = true;
        _sidTextBox.BorderStyle = BorderStyle.None;
        _sidTextBox.ForeColor = SystemColors.GrayText;
        _sidTextBox.Visible = false;

        // _passwordLabel (position set in code)
        _passwordLabel.Text = "Password:";
        _passwordLabel.AutoSize = true;
        _passwordLabel.Location = new Point(15, 67);

        // _passwordTextBox (position set in code)
        _passwordTextBox.Location = new Point(15, 89);
        _passwordTextBox.Size = new Size(330, 23);
        _passwordTextBox.UseSystemPasswordChar = true;

        // _statusLabel (position set in code)
        _statusLabel.Location = new Point(15, 181);
        _statusLabel.Size = new Size(330, 20);
        _statusLabel.ForeColor = Color.Red;

        // _createButton (add mode only, hidden in edit mode; position set in code)
        _createButton.Text = "Create...";
        _createButton.Location = new Point(100, 211);
        _createButton.Size = new Size(75, 28);
        _createButton.FlatStyle = FlatStyle.System;
        _createButton.Enabled = false;
        _createButton.Visible = false;
        _createButton.Click += OnCreateClick;

        // _okButton (position and text set in code)
        _okButton.Text = "OK";
        _okButton.Location = new Point(185, 211);
        _okButton.Size = new Size(75, 28);
        _okButton.FlatStyle = FlatStyle.System;
        _okButton.Enabled = false;
        _okButton.Click += OnOkClick;

        // _cancelButton (position set in code)
        _cancelButton.Text = "Cancel";
        _cancelButton.Location = new Point(270, 211);
        _cancelButton.DialogResult = DialogResult.Cancel;
        _cancelButton.Size = new Size(75, 28);
        _cancelButton.FlatStyle = FlatStyle.System;

        // CredentialEditDialog
        ClientSize = new Size(360, 254);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Controls.AddRange(new Control[]
        {
            _usernameLabel, _usernameComboBox, _sidTextBox,
            _passwordLabel, _passwordTextBox,
            _statusLabel, _createButton, _okButton, _cancelButton
        });

        ResumeLayout(false);
        PerformLayout();
    }
}
