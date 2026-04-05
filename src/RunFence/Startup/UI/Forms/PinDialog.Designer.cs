#nullable disable

using System.ComponentModel;

namespace RunFence.Startup.UI.Forms;

partial class PinDialog
{
    private IContainer components = null;

    private Label _promptLabel;
    private Panel _inputPanel;
    private Label _pinLabel;
    private TextBox _pinTextBox;
    private Label _confirmLabel;
    private TextBox _confirmPinTextBox;
    private Label _statusLabel;
    private Button _okButton;
    private Button _cancelButton;
    private LinkLabel _forgotLink;

    private PinDialog() { InitializeComponent(); }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _pinTextBox?.Clear();
            _confirmPinTextBox?.Clear();
            components?.Dispose();
        }
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        _promptLabel = new Label();
        _inputPanel = new Panel();
        _pinLabel = new Label();
        _pinTextBox = new TextBox();
        _confirmLabel = new Label();
        _confirmPinTextBox = new TextBox();
        _okButton = new Button();
        _cancelButton = new Button();
        _forgotLink = new LinkLabel();
        _statusLabel = new Label();

        _inputPanel.SuspendLayout();
        SuspendLayout();

        // _promptLabel (on form, hidden by default)
        _promptLabel.Location = new Point(15, 10);
        _promptLabel.MaximumSize = new Size(300, 0);
        _promptLabel.AutoSize = true;
        _promptLabel.ForeColor = SystemColors.ControlText;
        _promptLabel.Visible = false;

        // _inputPanel (maximum layout: Set mode + prompt, at y=35 placeholder)
        _inputPanel.Location = new Point(0, 35);
        _inputPanel.Size = new Size(330, 197);
        _inputPanel.Controls.AddRange(new Control[]
        {
            _pinLabel, _pinTextBox, _confirmLabel, _confirmPinTextBox,
            _okButton, _cancelButton, _forgotLink
        });

        // _pinLabel (inside inputPanel)
        _pinLabel.Text = "New PIN (digits, letters, special):";
        _pinLabel.Location = new Point(15, 15);
        _pinLabel.AutoSize = true;

        // _pinTextBox (inside inputPanel)
        _pinTextBox.Location = new Point(15, 37);
        _pinTextBox.Size = new Size(300, 23);
        _pinTextBox.UseSystemPasswordChar = true;

        // _confirmLabel (inside inputPanel, hidden by default)
        _confirmLabel.Text = "Confirm PIN:";
        _confirmLabel.Location = new Point(15, 72);
        _confirmLabel.AutoSize = true;
        _confirmLabel.Visible = false;

        // _confirmPinTextBox (inside inputPanel, hidden by default)
        _confirmPinTextBox.Location = new Point(15, 94);
        _confirmPinTextBox.Size = new Size(300, 23);
        _confirmPinTextBox.UseSystemPasswordChar = true;
        _confirmPinTextBox.Visible = false;

        // _okButton (inside inputPanel, placeholder position — set in code)
        _okButton.Text = "OK";
        _okButton.DialogResult = DialogResult.None;
        _okButton.Location = new Point(155, 154);
        _okButton.Size = new Size(75, 28);
        _okButton.FlatStyle = FlatStyle.System;
        _okButton.Click += OnOkClick;

        // _cancelButton (inside inputPanel, placeholder position — set in code)
        _cancelButton.Text = "Cancel";
        _cancelButton.DialogResult = DialogResult.Cancel;
        _cancelButton.Location = new Point(240, 154);
        _cancelButton.Size = new Size(75, 28);
        _cancelButton.FlatStyle = FlatStyle.System;

        // _forgotLink (inside inputPanel, hidden by default — position set in code)
        _forgotLink.Text = "Forgot PIN?";
        _forgotLink.Location = new Point(15, 159);
        _forgotLink.AutoSize = true;
        _forgotLink.Visible = false;

        // _statusLabel (on form, placeholder position — set in code via BringToFront)
        _statusLabel.Location = new Point(15, 129);
        _statusLabel.Size = new Size(300, 20);
        _statusLabel.ForeColor = Color.Red;

        // PinDialog
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        TopMost = true;
        ClientSize = new Size(330, 232);
        AcceptButton = _okButton;
        CancelButton = _cancelButton;
        Controls.Add(_promptLabel);
        Controls.Add(_inputPanel);
        Controls.Add(_statusLabel);

        _inputPanel.ResumeLayout(false);
        _inputPanel.PerformLayout();
        ResumeLayout(false);
        PerformLayout();
    }
}
