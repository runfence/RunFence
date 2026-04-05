#nullable disable

using System.ComponentModel;

namespace RunFence.UI.Forms;

partial class InputPromptDialog
{
    private IContainer components = null;

    private Label _promptLabel;
    private TextBox _inputTextBox;
    private Button _okButton;
    private Button _cancelButton;

    private InputPromptDialog() { InitializeComponent(); }

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
        _okButton = new Button();
        _cancelButton = new Button();

        SuspendLayout();

        // _promptLabel
        _promptLabel.Location = new Point(12, 12);
        _promptLabel.Size = new Size(456, 36);
        _promptLabel.AutoSize = false;

        // _inputTextBox
        _inputTextBox.Location = new Point(12, 54);
        _inputTextBox.Size = new Size(456, 23);
        _inputTextBox.MaxLength = 253;

        // _okButton
        _okButton.Text = "OK";
        _okButton.Location = new Point(312, 90);
        _okButton.Size = new Size(75, 28);
        _okButton.FlatStyle = FlatStyle.System;
        _okButton.Click += OnOkClick;

        // _cancelButton
        _cancelButton.Text = "Cancel";
        _cancelButton.Location = new Point(393, 90);
        _cancelButton.Size = new Size(75, 28);
        _cancelButton.FlatStyle = FlatStyle.System;
        _cancelButton.DialogResult = DialogResult.Cancel;

        // InputPromptDialog
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(480, 128);
        AcceptButton = _okButton;
        CancelButton = _cancelButton;
        Controls.AddRange(new Control[] { _promptLabel, _inputTextBox, _okButton, _cancelButton });

        ResumeLayout(false);
        PerformLayout();
    }
}
