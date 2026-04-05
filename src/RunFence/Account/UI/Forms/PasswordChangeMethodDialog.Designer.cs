#nullable disable

using System.ComponentModel;

namespace RunFence.Account.UI.Forms;

partial class PasswordChangeMethodDialog
{
    private IContainer components = null;

    private Label _messageLabel;
    private Label _pwLabel;
    private TextBox _passwordTextBox;
    private Button _changeButton;
    private Button _forceResetButton;
    private Button _cancelButton;

    private PasswordChangeMethodDialog() { InitializeComponent(); }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            components?.Dispose();
            _passwordTextBox.Clear();
        }
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        _messageLabel = new Label();
        _pwLabel = new Label();
        _passwordTextBox = new TextBox();
        _changeButton = new Button();
        _forceResetButton = new Button();
        _cancelButton = new Button();

        SuspendLayout();

        // _messageLabel
        _messageLabel.Text = "The password cannot be changed automatically. Enter the current password to change it, or force an admin reset (destructive \u2014 loses EFS and saved credentials).";
        _messageLabel.Location = new Point(15, 15);
        _messageLabel.Size = new Size(330, 50);

        // _pwLabel
        _pwLabel.Text = "Current password:";
        _pwLabel.Location = new Point(15, 72);
        _pwLabel.AutoSize = true;

        // _passwordTextBox
        _passwordTextBox.Location = new Point(15, 94);
        _passwordTextBox.Size = new Size(330, 23);
        _passwordTextBox.UseSystemPasswordChar = true;
        _passwordTextBox.TextChanged += OnPasswordTextChanged;

        // _changeButton
        _changeButton.Text = "Change";
        _changeButton.Location = new Point(15, 130);
        _changeButton.Size = new Size(75, 28);
        _changeButton.FlatStyle = FlatStyle.System;
        _changeButton.Click += OnChangeClick;

        // _forceResetButton
        _forceResetButton.Text = "Force Reset";
        _forceResetButton.Location = new Point(165, 130);
        _forceResetButton.Size = new Size(90, 28);
        _forceResetButton.FlatStyle = FlatStyle.System;
        _forceResetButton.Click += OnForceResetClick;

        // _cancelButton
        _cancelButton.Text = "Cancel";
        _cancelButton.DialogResult = DialogResult.Cancel;
        _cancelButton.Location = new Point(270, 130);
        _cancelButton.Size = new Size(75, 28);
        _cancelButton.FlatStyle = FlatStyle.System;

        // PasswordChangeMethodDialog
        Text = "Change Password";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(360, 175);
        AcceptButton = _changeButton;
        CancelButton = _cancelButton;
        Controls.AddRange(new Control[]
        {
            _messageLabel, _pwLabel, _passwordTextBox,
            _changeButton, _forceResetButton, _cancelButton
        });

        ResumeLayout(false);
        PerformLayout();
    }
}
