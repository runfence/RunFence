#nullable disable

using System.ComponentModel;

namespace RunFence.RunAs.UI.Forms;

partial class CallerIdentityDialog
{
    private IContainer components = null;

    private Label _promptLabel;
    private ComboBox _identityComboBox;
    private Label _statusLabel;
    private Button _okButton;
    private Button _cancelButton;

    private CallerIdentityDialog() { InitializeComponent(); }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            components?.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        _promptLabel = new Label();
        _identityComboBox = new ComboBox();
        _statusLabel = new Label();
        _okButton = new Button();
        _cancelButton = new Button();

        SuspendLayout();

        // _promptLabel
        _promptLabel.Text = "Enter DOMAIN\\Username or Username:";
        _promptLabel.Location = new Point(15, 15);
        _promptLabel.AutoSize = true;

        // _identityComboBox
        _identityComboBox.Location = new Point(15, 40);
        _identityComboBox.Size = new Size(350, 23);
        _identityComboBox.DropDownStyle = ComboBoxStyle.DropDown;
        _identityComboBox.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
        _identityComboBox.AutoCompleteSource = AutoCompleteSource.ListItems;

        // _statusLabel
        _statusLabel.Location = new Point(15, 70);
        _statusLabel.Size = new Size(350, 20);
        _statusLabel.ForeColor = Color.Red;

        // _okButton
        _okButton.Text = "OK";
        _okButton.Location = new Point(210, 100);
        _okButton.Size = new Size(75, 28);
        _okButton.FlatStyle = FlatStyle.System;
        _okButton.Click += OnOkClick;

        // _cancelButton
        _cancelButton.Text = "Cancel";
        _cancelButton.DialogResult = DialogResult.Cancel;
        _cancelButton.Location = new Point(295, 100);
        _cancelButton.Size = new Size(75, 28);
        _cancelButton.FlatStyle = FlatStyle.System;

        // CallerIdentityDialog
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        Text = "Add Allowed Caller";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(385, 143);
        AcceptButton = _okButton;
        CancelButton = _cancelButton;
        Controls.AddRange(new Control[] { _promptLabel, _identityComboBox, _statusLabel, _okButton, _cancelButton });

        ResumeLayout(false);
        PerformLayout();
    }
}
