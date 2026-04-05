#nullable disable

using System.ComponentModel;

namespace RunFence.UI.Forms;

partial class ManualSidEntryDialog
{
    private IContainer components = null;

    private Label _infoLabel;
    private TextBox _sidTextBox;
    private Label _errorLabel;
    private Button _okButton;
    private Button _cancelButton;

    private ManualSidEntryDialog() { InitializeComponent(); }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            components?.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        _infoLabel = new Label();
        _sidTextBox = new TextBox();
        _errorLabel = new Label();
        _okButton = new Button();
        _cancelButton = new Button();

        SuspendLayout();

        // _infoLabel
        _infoLabel.Location = new Point(15, 10);
        _infoLabel.Size = new Size(370, 45);

        // _sidTextBox
        _sidTextBox.Location = new Point(15, 60);
        _sidTextBox.Size = new Size(370, 23);

        // _errorLabel
        _errorLabel.Location = new Point(15, 88);
        _errorLabel.Size = new Size(370, 20);
        _errorLabel.ForeColor = Color.Red;

        // _okButton
        _okButton.Text = "OK";
        _okButton.Location = new Point(225, 115);
        _okButton.Size = new Size(75, 28);
        _okButton.FlatStyle = FlatStyle.System;
        _okButton.Click += OnOkClick;

        // _cancelButton
        _cancelButton.Text = "Cancel";
        _cancelButton.DialogResult = DialogResult.Cancel;
        _cancelButton.Location = new Point(310, 115);
        _cancelButton.Size = new Size(75, 28);
        _cancelButton.FlatStyle = FlatStyle.System;

        // ManualSidEntryDialog
        Text = "Manual SID Entry";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(400, 170);
        AcceptButton = _okButton;
        CancelButton = _cancelButton;
        Controls.AddRange(new Control[] { _infoLabel, _sidTextBox, _errorLabel, _okButton, _cancelButton });

        ResumeLayout(false);
        PerformLayout();
    }
}
