#nullable disable

using System.ComponentModel;

namespace RunFence.Groups.UI.Forms;

partial class CreateGroupDialog
{
    private IContainer components = null;

    private Label _nameLabel;
    private TextBox _nameTextBox;
    private Label _statusLabel;
    private Button _okButton;
    private Button _cancelButton;

    private CreateGroupDialog() { InitializeComponent(); }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            components?.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        _nameLabel = new Label();
        _nameTextBox = new TextBox();
        _statusLabel = new Label();
        _okButton = new Button();
        _cancelButton = new Button();

        SuspendLayout();

        // _nameLabel
        _nameLabel.Text = "Group name:";
        _nameLabel.Location = new Point(12, 14);
        _nameLabel.Size = new Size(90, 20);
        _nameLabel.AutoSize = false;

        // _nameTextBox
        _nameTextBox.Location = new Point(108, 12);
        _nameTextBox.Size = new Size(270, 23);
        _nameTextBox.MaxLength = 256;

        // _statusLabel
        _statusLabel.Location = new Point(12, 44);
        _statusLabel.Size = new Size(366, 20);
        _statusLabel.AutoSize = false;
        _statusLabel.ForeColor = Color.Red;

        // _okButton
        _okButton.Text = "OK";
        _okButton.Location = new Point(213, 72);
        _okButton.Size = new Size(75, 28);
        _okButton.FlatStyle = FlatStyle.System;
        _okButton.Click += OnOkClick;

        // _cancelButton
        _cancelButton.Text = "Cancel";
        _cancelButton.Location = new Point(294, 72);
        _cancelButton.Size = new Size(75, 28);
        _cancelButton.FlatStyle = FlatStyle.System;
        _cancelButton.DialogResult = DialogResult.Cancel;

        // CreateGroupDialog
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Text = "Create Group";
        ClientSize = new Size(390, 110);
        AcceptButton = _okButton;
        CancelButton = _cancelButton;
        Controls.AddRange(new Control[]
        {
            _nameLabel, _nameTextBox,
            _statusLabel, _okButton, _cancelButton
        });

        ResumeLayout(false);
        PerformLayout();
    }
}
