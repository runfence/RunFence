#nullable disable

using System.ComponentModel;

namespace RunFence.Groups.UI.Forms;

partial class GroupMemberPickerDialog
{
    private IContainer components = null;

    private Label _promptLabel;
    private CheckedListBox _membersListBox;
    private Button _okButton;
    private Button _cancelButton;

    private GroupMemberPickerDialog() { InitializeComponent(); }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            components?.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        _promptLabel = new Label();
        _membersListBox = new CheckedListBox();
        _okButton = new Button();
        _cancelButton = new Button();

        SuspendLayout();

        // _promptLabel
        _promptLabel.Location = new Point(12, 12);
        _promptLabel.Size = new Size(456, 20);
        _promptLabel.AutoSize = false;

        // _membersListBox
        _membersListBox.Location = new Point(12, 36);
        _membersListBox.Size = new Size(456, 220);
        _membersListBox.CheckOnClick = true;

        // _okButton
        _okButton.Text = "OK";
        _okButton.Location = new Point(312, 266);
        _okButton.Size = new Size(75, 28);
        _okButton.FlatStyle = FlatStyle.System;
        _okButton.Click += OnOkClick;

        // _cancelButton
        _cancelButton.Text = "Cancel";
        _cancelButton.Location = new Point(393, 266);
        _cancelButton.Size = new Size(75, 28);
        _cancelButton.FlatStyle = FlatStyle.System;
        _cancelButton.DialogResult = DialogResult.Cancel;

        // GroupMemberPickerDialog
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(480, 304);
        AcceptButton = _okButton;
        CancelButton = _cancelButton;
        Controls.AddRange(new Control[] { _promptLabel, _membersListBox, _okButton, _cancelButton });

        ResumeLayout(false);
        PerformLayout();
    }
}
