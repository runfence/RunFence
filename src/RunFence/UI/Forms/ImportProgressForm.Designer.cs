#nullable disable

using System.ComponentModel;

namespace RunFence.UI.Forms;

partial class ImportProgressForm
{
    private IContainer components = null;

    private TextBox _logTextBox;
    private Button _okButton;

    private ImportProgressForm() { InitializeComponent(); }

    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null))
            components.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        _logTextBox = new TextBox();
        _okButton = new Button();
        SuspendLayout();

        _logTextBox.Dock = DockStyle.Fill;
        _logTextBox.Multiline = true;
        _logTextBox.ReadOnly = true;
        _logTextBox.ScrollBars = ScrollBars.Both;
        _logTextBox.WordWrap = false;

        _okButton.Text = "OK";
        _okButton.Dock = DockStyle.Bottom;
        _okButton.Height = 35;
        _okButton.DialogResult = DialogResult.OK;
        _okButton.FlatStyle = FlatStyle.System;
        _okButton.Enabled = false;

        Text = "Import Progress";
        ClientSize = new Size(500, 350);
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(350, 250);
        AcceptButton = _okButton;
        Controls.Add(_logTextBox);
        Controls.Add(_okButton);

        ResumeLayout(false);
    }
}
