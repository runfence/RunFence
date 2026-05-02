using System.ComponentModel;
using RunFence.Account.UI;
using RunFence.Apps.UI;
using RunFence.Core;
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
    private readonly bool _allowReset;
    private readonly bool _exitOnCancel;
    private readonly string? _promptMessage;
    private readonly OperationGuard _guard = new();

    private SecurePasswordBox _passwordSecure = null!;
    private SecurePasswordBox? _confirmPasswordSecure;

    public bool ResetRequested { get; private set; }

    /// <summary>
    /// For Verify mode: async callback that returns true if PIN is correct.
    /// Runs on a background thread to avoid blocking the UI during Argon2id derivation.
    /// </summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Func<ProtectedString, bool>? VerifyCallback { get; set; }

    /// <summary>
    /// Optional async callback that runs crypto operations while the dialog stays open
    /// with a "Processing..." status. Takes (enteredPin, null) and returns null on success
    /// or an error message on failure. The dialog closes only on success.
    /// In Verify mode, runs after <see cref="VerifyCallback"/> succeeds.
    /// </summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Func<ProtectedString, string?, Task<string?>>? ProcessingCallback { get; set; }

    public PinDialog(PinDialogMode mode, string? promptMessage = null, bool allowReset = true, bool exitOnCancel = false)
    {
        _mode = mode;
        _allowReset = allowReset;
        _exitOnCancel = exitOnCancel;
        _promptMessage = promptMessage;
        InitializeComponent();
        Icon = AppIcons.GetAppIcon();
        ConfigureLayout();
        Shown += (_, _) => Activate();
#pragma warning disable CS0162
        if (DebugHelper.IsDebugBuild)
            Shown += (_, _) =>
            {
                using var debugPin = ProtectedString.FromChars("1111".AsSpan());
                _passwordSecure.SetFromProtectedString(debugPin);
                _pinTextBox.SelectAll();
                _pinTextBox.Focus();
                if (_mode == PinDialogMode.Set)
                    _confirmPasswordSecure?.SetFromProtectedString(debugPin);
            };
#pragma warning restore CS0162
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
        _passwordSecure = new SecurePasswordBox(_pinTextBox);
        _passwordSecure.AddEyeToggle();
        yPos += 35; // after pinTextBox

        if (_mode == PinDialogMode.Set)
        {
            _confirmLabel.Visible = true;
            yPos += 22; // after confirmLabel
            _confirmPasswordSecure = new SecurePasswordBox(_confirmPinTextBox);
            _confirmPasswordSecure.AddEyeToggle();
            _confirmPinTextBox.Visible = true;
            yPos += 35; // after confirmTextBox
        }

        var statusY = yPos;
        yPos += 25;

        _okButton.Location = new Point(155, yPos);
        _cancelButton.Location = new Point(240, yPos);

        if (_mode == PinDialogMode.Verify)
        {
            if (_exitOnCancel)
                _cancelButton.Text = "Exit";
            _forgotLink.Visible = _allowReset;
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
        _statusLabel.Location = new Point(15, labelHeight + statusY);
        _statusLabel.BringToFront();
    }

    private async void OnOkClick(object? sender, EventArgs e)
    {
        _statusLabel.Text = "";

        if (_passwordSecure.IsEmpty)
        {
            _statusLabel.Text = "PIN is required.";
            return;
        }

        switch (_mode)
        {
            case PinDialogMode.Set when _passwordSecure.GetPasswordLength() < MinPinLength:
                _statusLabel.Text = $"PIN must be at least {MinPinLength} characters.";
                return;
            case PinDialogMode.Set when
                _confirmPasswordSecure != null &&
                !_passwordSecure.PasswordsMatch(_confirmPasswordSecure):
                _statusLabel.Text = "PINs do not match.";
                return;
            case PinDialogMode.Verify when VerifyCallback != null:
            {
                _guard.Begin(_inputPanel);
                bool verified;
                try
                {
                    _statusLabel.ForeColor = Color.DarkBlue;
                    _statusLabel.Text = "Verifying PIN...";

                    var callback = VerifyCallback;
                    using var pin = _passwordSecure.GetPassword();
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
                    _passwordSecure.Clear();
                    _pinTextBox.Focus();
                    return;
                }

                break;
            }
        }

        if (ProcessingCallback != null)
        {
            _guard.Begin(_inputPanel);
            string? error;
            try
            {
                _statusLabel.ForeColor = Color.DarkBlue;
                _statusLabel.Text = "Processing...";

                var pin = _passwordSecure.GetPassword();
                try
                {
                    error = await ProcessingCallback(pin, null);
                }
                finally
                {
                    pin.Dispose();
                }

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

        DialogResult = DialogResult.OK;
        Close();
    }
}
