#nullable disable

using System.ComponentModel;

namespace RunFence.Groups.UI.Forms;

partial class ManualMemberEntryDialog
{
    private IContainer components = null;

    private Label _promptLabel;
    private TextBox _inputTextBox;
    private Label _errorLabel;
    private Button _okButton;
    private Button _cancelButton;

    internal ManualMemberEntryDialog() { InitializeComponent(); }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            components?.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        _promptLabel = new Label();
        _inputTextBox = new TextBox();
        _errorLabel = new Label();
        _okButton = new Button();
        _cancelButton = new Button();

        SuspendLayout();

        // _promptLabel
        _promptLabel.Text = "Enter a username (e.g. DOMAIN\\user) or SID (e.g. S-1-5-21-...):";
        _promptLabel.Location = new Point(12, 12);
        _promptLabel.Size = new Size(380, 32);
        _promptLabel.AutoSize = false;

        // _inputTextBox
        _inputTextBox.Location = new Point(12, 52);
        _inputTextBox.Size = new Size(380, 23);

        // _errorLabel
        _errorLabel.Location = new Point(12, 82);
        _errorLabel.Size = new Size(380, 20);
        _errorLabel.ForeColor = Color.Red;
        _errorLabel.AutoSize = false;

        // _okButton
        _okButton.Text = "OK";
        _okButton.Location = new Point(236, 112);
        _okButton.Size = new Size(75, 28);
        _okButton.FlatStyle = FlatStyle.System;
        _okButton.Click += OnOkClick;

        // _cancelButton
        _cancelButton.Text = "Cancel";
        _cancelButton.Location = new Point(317, 112);
        _cancelButton.Size = new Size(75, 28);
        _cancelButton.FlatStyle = FlatStyle.System;
        _cancelButton.DialogResult = DialogResult.Cancel;

        // ManualMemberEntryDialog
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(404, 152);
        AcceptButton = _okButton;
        CancelButton = _cancelButton;
        Text = "Add Account Manually";
        Controls.AddRange(new Control[] { _promptLabel, _inputTextBox, _errorLabel, _okButton, _cancelButton });

        ResumeLayout(false);
        PerformLayout();
    }
}
