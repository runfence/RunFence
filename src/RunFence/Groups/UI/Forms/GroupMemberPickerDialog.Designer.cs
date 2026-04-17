#nullable disable

using System.ComponentModel;

namespace RunFence.Groups.UI.Forms;

partial class GroupMemberPickerDialog
{
    private IContainer components = null;

    private Label _promptLabel;
    private TextBox _searchTextBox;
    private CheckedListBox _membersListBox;
    private Button _addManuallyButton;
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
        _searchTextBox = new TextBox();
        _membersListBox = new CheckedListBox();
        _addManuallyButton = new Button();
        _okButton = new Button();
        _cancelButton = new Button();

        SuspendLayout();

        // _promptLabel
        _promptLabel.Location = new Point(12, 12);
        _promptLabel.Size = new Size(456, 20);
        _promptLabel.AutoSize = false;

        // _searchTextBox
        _searchTextBox.Location = new Point(12, 36);
        _searchTextBox.Size = new Size(456, 23);
        _searchTextBox.PlaceholderText = "Filter accounts...";
        _searchTextBox.TextChanged += OnSearchTextChanged;

        // _membersListBox
        _membersListBox.Location = new Point(12, 63);
        _membersListBox.Size = new Size(456, 200);
        _membersListBox.CheckOnClick = true;
        _membersListBox.ItemCheck += OnMembersListBoxItemCheck;

        // _addManuallyButton
        _addManuallyButton.Text = "Add manually...";
        _addManuallyButton.Location = new Point(12, 272);
        _addManuallyButton.Size = new Size(110, 28);
        _addManuallyButton.FlatStyle = FlatStyle.System;
        _addManuallyButton.Click += OnAddManuallyClick;

        // _okButton
        _okButton.Text = "OK";
        _okButton.Location = new Point(312, 272);
        _okButton.Size = new Size(75, 28);
        _okButton.FlatStyle = FlatStyle.System;
        _okButton.Click += OnOkClick;

        // _cancelButton
        _cancelButton.Text = "Cancel";
        _cancelButton.Location = new Point(393, 272);
        _cancelButton.Size = new Size(75, 28);
        _cancelButton.FlatStyle = FlatStyle.System;
        _cancelButton.DialogResult = DialogResult.Cancel;

        // GroupMemberPickerDialog
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(480, 312);
        AcceptButton = _okButton;
        CancelButton = _cancelButton;
        Controls.AddRange(new Control[] { _promptLabel, _searchTextBox, _membersListBox, _addManuallyButton, _okButton, _cancelButton });

        ResumeLayout(false);
        PerformLayout();
    }
}
