using System.ComponentModel;
using RunFence.Account.UI;
using RunFence.Apps.UI;
using RunFence.Infrastructure;

namespace RunFence.Startup.UI.Forms;

public enum PinDialogMode
{
    Set,
    Verify
}

public partial class PinDialog : Form
{
    private const int MinPinLength = 4;

    private readonly PinDialogMode _mode;
    private readonly string? _promptMessage;
    private readonly OperationGuard _guard = new();

    private string EnteredPin { get; set; } = string.Empty;
    public bool ResetRequested { get; private set; }

    /// <summary>
    /// For Verify mode: async callback that returns true if PIN is correct.
    /// Runs on a background thread to avoid blocking the UI during Argon2id derivation.
    /// </summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Func<string, bool>? VerifyCallback { get; set; }

    /// <summary>
    /// For Set mode: async callback that runs crypto operations while the dialog
    /// stays open with a "Processing..." status. Takes (enteredPin, null) and returns
    /// null on success or an error message on failure. The dialog closes only on success.
    /// </summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Func<string, string?, Task<string?>>? ProcessingCallback { get; set; }

    public PinDialog(PinDialogMode mode, string? promptMessage = null)
    {
        _mode = mode;
        _promptMessage = promptMessage;
        InitializeComponent();
        Icon = AppIcons.GetAppIcon();
        ConfigureLayout();
        Shown += (_, _) => Activate();
    }

    private void ConfigureLayout()
    {
        Text = _mode switch
        {
            PinDialogMode.Set => "Set PIN - RunFence",
            PinDialogMode.Verify => "Enter PIN - RunFence",
            _ => "PIN - RunFence"
        };

        _pinLabel.Text = _mode == PinDialogMode.Verify ? "PIN:" : "New PIN (digits, letters, special):";

        int labelHeight = 0;
        if (_promptMessage != null)
        {
            _promptLabel.Text = _promptMessage;
            _promptLabel.Visible = true;
            // Let the label compute its size before reading Bottom
            _promptLabel.CreateControl();
            labelHeight = _promptLabel.Bottom + 8;
            _inputPanel.Location = new Point(0, labelHeight);
        }
        else
        {
            _inputPanel.Location = new Point(0, 0);
        }

        var yPos = 15; // pinLabel row
        yPos += 22; // after pinLabel
        PasswordEyeToggle.AddTo(_pinTextBox);
        yPos += 35; // after pinTextBox

        if (_mode == PinDialogMode.Set)
        {
            _confirmLabel.Visible = true;
            yPos += 22; // after confirmLabel
            PasswordEyeToggle.AddTo(_confirmPinTextBox);
            _confirmPinTextBox.Visible = true;
            yPos += 35; // after confirmTextBox
        }

        var statusY = yPos;
        yPos += 25;

        _okButton.Location = new Point(155, yPos);
        _cancelButton.Location = new Point(240, yPos);

        if (_mode == PinDialogMode.Verify)
        {
            _cancelButton.Text = "Exit";
            _forgotLink.Visible = true;
            _forgotLink.Location = new Point(15, yPos + 5);
            _forgotLink.LinkClicked += (_, _) =>
            {
                ResetRequested = true;
                DialogResult = DialogResult.Retry;
                Close();
            };
        }

        ClientSize = new Size(330, labelHeight + yPos + 43);
        _inputPanel.Size = new Size(330, yPos + 43);
        _statusLabel.Location = new Point(15, statusY);
        _statusLabel.BringToFront();
    }

    private async void OnOkClick(object? sender, EventArgs e)
    {
        _statusLabel.Text = "";

        if (string.IsNullOrEmpty(_pinTextBox.Text))
        {
            _statusLabel.Text = "PIN is required.";
            return;
        }

        if (_mode == PinDialogMode.Set && _pinTextBox.Text.Length < MinPinLength)
        {
            _statusLabel.Text = $"PIN must be at least {MinPinLength} characters.";
            return;
        }

        if (_mode == PinDialogMode.Set &&
            _confirmPinTextBox != null &&
            _pinTextBox.Text != _confirmPinTextBox.Text)
        {
            _statusLabel.Text = "PINs do not match.";
            return;
        }

        if (_mode == PinDialogMode.Verify && VerifyCallback != null)
        {
            var pin = _pinTextBox.Text;
            _guard.Begin(_inputPanel);
            bool verified;
            try
            {
                _statusLabel.ForeColor = Color.DarkBlue;
                _statusLabel.Text = "Verifying PIN...";

                var callback = VerifyCallback;
                verified = await Task.Run(() => callback(pin));

                if (IsDisposed)
                    return;
            }
            finally
            {
                if (!_inputPanel.IsDisposed)
                    _guard.End(_inputPanel);
            }

            _statusLabel.ForeColor = Color.Red;

            if (!verified)
            {
                _statusLabel.Text = "Incorrect PIN.";
                _pinTextBox.Clear();
                _pinTextBox.Focus();
                return;
            }
        }

        EnteredPin = _pinTextBox.Text;

        if (_mode == PinDialogMode.Set && ProcessingCallback != null)
        {
            _guard.Begin(_inputPanel);
            string? error;
            try
            {
                _statusLabel.ForeColor = Color.DarkBlue;
                _statusLabel.Text = "Processing...";

                error = await ProcessingCallback(EnteredPin, null);

                if (IsDisposed)
                    return;
            }
            finally
            {
                if (!_inputPanel.IsDisposed)
                    _guard.End(_inputPanel);
            }

            if (error != null)
            {
                _statusLabel.ForeColor = Color.Red;
                _statusLabel.Text = error;
                return;
            }
        }

        _pinTextBox.Clear();
        DialogResult = DialogResult.OK;
        Close();
    }
}