#nullable disable

using System.ComponentModel;

namespace RunFence.RunAs.UI.Forms;

partial class RunAsPasswordDialog
{
    private IContainer components = null;
    private Label _accountLabel;
    private TextBox _passwordTextBox;
    private CheckBox _rememberCheckBox;
    private Label _statusLabel;
    private Button _okButton;
    private Button _cancelButton;

    private RunAsPasswordDialog() { InitializeComponent(); }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _passwordTextBox?.Clear();
            components?.Dispose();
        }
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        _accountLabel = new Label();
        _passwordTextBox = new TextBox();
        _rememberCheckBox = new CheckBox();
        _statusLabel = new Label();
        _okButton = new Button();
        _cancelButton = new Button();

        SuspendLayout();

        // _accountLabel
        _accountLabel.AutoSize = true;
        _accountLabel.MaximumSize = new Size(316, 0);
        _accountLabel.Location = new Point(12, 15);

        // _passwordTextBox
        _passwordTextBox.UseSystemPasswordChar = true;
        _passwordTextBox.Size = new Size(316, 23);
        _passwordTextBox.Location = new Point(12, 42);

        // _rememberCheckBox
        _rememberCheckBox.Text = "Remember password";
        _rememberCheckBox.AutoSize = true;
        _rememberCheckBox.Location = new Point(12, 74);

        // _statusLabel
        _statusLabel.AutoSize = false;
        _statusLabel.Size = new Size(316, 35);
        _statusLabel.Location = new Point(12, 98);
        _statusLabel.ForeColor = Color.Red;

        // _okButton
        _okButton.Text = "OK";
        _okButton.Size = new Size(80, 28);
        _okButton.Location = new Point(167, 140);
        _okButton.FlatStyle = FlatStyle.System;
        _okButton.Click += OnOkClick;

        // _cancelButton
        _cancelButton.Text = "Cancel";
        _cancelButton.Size = new Size(80, 28);
        _cancelButton.Location = new Point(255, 140);
        _cancelButton.FlatStyle = FlatStyle.System;
        _cancelButton.Click += OnCancelClick;

        // RunAsPasswordDialog
        Text = "Enter Password - RunFence";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        TopMost = true;
        ClientSize = new Size(340, 178);
        AcceptButton = _okButton;
        CancelButton = _cancelButton;
        Controls.AddRange(new Control[] { _accountLabel, _passwordTextBox, _rememberCheckBox, _statusLabel, _okButton, _cancelButton });

        ResumeLayout(false);
        PerformLayout();
    }
}
