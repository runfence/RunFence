#nullable disable

using System.ComponentModel;

namespace RunFence.Account.UI.Forms;

partial class PasswordInputDialog
{
    private IContainer components = null;

    private Label _promptLabel;
    private TextBox _passwordTextBox;
    private Button _okButton;
    private Button _cancelButton;

    private PasswordInputDialog() { InitializeComponent(); }

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
        _promptLabel = new Label();
        _passwordTextBox = new TextBox();
        _okButton = new Button();
        _cancelButton = new Button();
        SuspendLayout();

        _promptLabel.Location = new Point(12, 16);
        _promptLabel.Size = new Size(360, 32);
        _promptLabel.AutoSize = false;

        _passwordTextBox.Location = new Point(12, 55);
        _passwordTextBox.Size = new Size(360, 23);
        _passwordTextBox.UseSystemPasswordChar = true;

        _okButton.Location = new Point(213, 90);
        _okButton.Size = new Size(75, 26);
        _okButton.Text = "OK";
        _okButton.Click += OnOkClick;

        _cancelButton.Location = new Point(294, 90);
        _cancelButton.Size = new Size(75, 26);
        _cancelButton.Text = "Cancel";
        _cancelButton.Click += OnCancelClick;

        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(384, 128);
        Controls.Add(_promptLabel);
        Controls.Add(_passwordTextBox);
        Controls.Add(_okButton);
        Controls.Add(_cancelButton);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterScreen;
        Text = "Enter Password";
        AcceptButton = _okButton;
        CancelButton = _cancelButton;
        ResumeLayout(false);
    }
}
