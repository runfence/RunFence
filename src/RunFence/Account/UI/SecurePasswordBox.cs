using RunFence.Core;

namespace RunFence.Account.UI;

/// <summary>
/// WinForms facade for secure password entry. The TextBox display is derived from an
/// internal ProtectedString-backed edit buffer; the TextBox never receives plaintext while hidden.
/// </summary>
public sealed class SecurePasswordBox : IDisposable
{
    internal const int WM_SETTEXT = 0x000C;
    internal const int EM_EMPTYUNDOBUFFER = 0x00CD;
    internal const int EM_SETMARGINS = 0xD3;
    internal const int EC_RIGHTMARGIN = 2;

    private readonly TextBox _textBox;
    private readonly SecurePasswordEditBuffer _editBuffer = new();
    private readonly SecurePasswordTextRenderer _renderer = new();
    private readonly ISecurePasswordClipboardService _clipboardService;
    private readonly SecurePasswordWndProcInterceptor _interceptor;
    private bool _revealed;
    private bool _suppressInterception;
    private Button? _eyeButton;
    private Image? _eyeOpen;
    private Image? _eyeSlashed;
    private bool _disposed;
    private Form? _cachedForm;

    public SecurePasswordBox(TextBox textBox)
        : this(textBox, new SecurePasswordClipboardService())
    {
    }

    internal SecurePasswordBox(TextBox textBox, ISecurePasswordClipboardService clipboardService)
    {
        _textBox = textBox;
        _clipboardService = clipboardService;
        _textBox.UseSystemPasswordChar = false;
        _textBox.PasswordChar = SecurePasswordTextRenderer.BulletChar;
        _textBox.ImeMode = ImeMode.Disable;
        _textBox.AllowDrop = false;

        _interceptor = new SecurePasswordWndProcInterceptor(this, _textBox);

        SendMessage(EM_EMPTYUNDOBUFFER, IntPtr.Zero, IntPtr.Zero);

        if (_textBox.IsHandleCreated)
            _cachedForm = _textBox.FindForm();
        else
            _textBox.HandleCreated += (_, _) => _cachedForm = _textBox.FindForm();

        _textBox.Disposed += (_, _) => Dispose();
    }

    public ProtectedString GetPassword() => _editBuffer.GetPassword();

    public int GetPasswordLength() => _editBuffer.Length;

    public bool IsEmpty => _editBuffer.IsEmpty;

    public bool PasswordsMatch(SecurePasswordBox other) =>
        _editBuffer.PasswordsMatch(other._editBuffer);

    public void SetFromProtectedString(ProtectedString? value)
    {
        _editBuffer.SetFromProtectedString(value);
        RefreshDisplay(resetPendingSurrogate: true);
    }

    public void Clear()
    {
        _editBuffer.Clear();
        RefreshDisplay(resetPendingSurrogate: true);
    }

    public void AddEyeToggle()
    {
        var btnWidth = _textBox.Height;
        var iconSize = Math.Max(12, _textBox.ClientSize.Height - 4);

        _eyeOpen = PasswordEyeToggle.CreateEyeImage(iconSize, slashed: false);
        _eyeSlashed = PasswordEyeToggle.CreateEyeImage(iconSize, slashed: true);

        _eyeButton = new Button
        {
            Size = _textBox.ClientSize with { Width = btnWidth },
            Location = new Point(_textBox.ClientSize.Width - btnWidth, 0),
            Anchor = AnchorStyles.Right | AnchorStyles.Top | AnchorStyles.Bottom,
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Default,
            TabStop = false,
            BackColor = SystemColors.Window,
            Image = _eyeOpen
        };
        _eyeButton.FlatAppearance.BorderSize = 0;
        _eyeButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(0xE8, 0xE8, 0xE8);
        _eyeButton.FlatAppearance.MouseDownBackColor = Color.FromArgb(0xD8, 0xD8, 0xD8);

        _eyeButton.Click += (_, _) => ToggleReveal();

        _textBox.Controls.Add(_eyeButton);

        PasswordEyeToggle.UpdateButtonForEnabledState(_eyeButton, _textBox.Enabled);
        _textBox.EnabledChanged += (_, _) => PasswordEyeToggle.UpdateButtonForEnabledState(_eyeButton!, _textBox.Enabled);

        if (_textBox.IsHandleCreated)
            ApplyRightMargin(btnWidth);
        else
            _textBox.HandleCreated += (_, _) => ApplyRightMargin(btnWidth);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        if (_revealed)
            PasswordEyeToggle.ApplyRevealAffinity(_cachedForm, false);

        _editBuffer.Dispose();
        _interceptor.ReleaseHandle();
        _eyeOpen?.Dispose();
        _eyeSlashed?.Dispose();
    }

    internal SecurePasswordEditResult HandleChar(char ch) =>
        _editBuffer.ApplyChar(ch, _textBox.SelectionStart, _textBox.SelectionLength, _textBox.MaxLength);

    internal SecurePasswordEditResult HandleDelete() =>
        _editBuffer.ApplyDelete(_textBox.SelectionStart, _textBox.SelectionLength, _textBox.MaxLength);

    internal SecurePasswordEditResult HandlePaste()
    {
        using var text = _clipboardService.OpenUnicodeText(out bool unicodeTextAvailable);
        if (text is null)
        {
            if (unicodeTextAvailable)
            {
                return _editBuffer.ApplyPaste(
                    SecurePasswordEmptyPasteSource.Instance,
                    _textBox.SelectionStart,
                    _textBox.SelectionLength,
                    _textBox.MaxLength);
            }

            return new SecurePasswordEditResult(false, _textBox.SelectionStart, _textBox.SelectionLength);
        }

        return _editBuffer.ApplyPaste(text, _textBox.SelectionStart, _textBox.SelectionLength, _textBox.MaxLength);
    }

    internal SecurePasswordEditResult HandleClear() =>
        _editBuffer.ApplyClear(_textBox.SelectionStart, _textBox.SelectionLength);

    internal SecurePasswordEditResult HandleCopy()
    {
        if (!_clipboardService.ShouldSuppressPasswordClipboardWrite())
            throw new InvalidOperationException("Secure password copy is not allowed to write to the clipboard.");

        return _editBuffer.ApplyCopy(_textBox.SelectionStart, _textBox.SelectionLength);
    }

    internal SecurePasswordEditResult HandleCut()
    {
        if (!_clipboardService.ShouldSuppressPasswordClipboardWrite())
            throw new InvalidOperationException("Secure password cut is not allowed to write to the clipboard.");

        return _editBuffer.ApplyCut(_textBox.SelectionStart, _textBox.SelectionLength);
    }

    internal SecurePasswordEditResult HandleUndo() =>
        _editBuffer.ApplyUndo(_textBox.SelectionStart, _textBox.SelectionLength);

    internal void ApplyEditResult(SecurePasswordEditResult result)
    {
        if (result.Changed)
            RefreshDisplay(resetPendingSurrogate: false);

        _textBox.SelectionStart = Math.Min(result.SelectionStart, _textBox.TextLength);
        _textBox.SelectionLength = Math.Min(result.SelectionLength, _textBox.TextLength - _textBox.SelectionStart);
        SendMessage(EM_EMPTYUNDOBUFFER, IntPtr.Zero, IntPtr.Zero);
    }

    private void ToggleReveal()
    {
        _revealed = !_revealed;
        _textBox.PasswordChar = _revealed ? '\0' : SecurePasswordTextRenderer.BulletChar;
        RefreshDisplay(resetPendingSurrogate: false);

        PasswordEyeToggle.ApplyRevealAffinity(_textBox, _revealed);

        if (_eyeButton is not null)
            _eyeButton.Image = _revealed ? _eyeSlashed : _eyeOpen;
    }

    private void RefreshDisplay(bool resetPendingSurrogate)
    {
        _suppressInterception = true;
        if (resetPendingSurrogate)
            _editBuffer.ClearPendingHighSurrogate();

        try
        {
            using var rendered = _renderer.Render(_editBuffer.Password, _revealed);
            PasswordEyeToggleNative.SendMessage(_textBox.Handle, WM_SETTEXT, IntPtr.Zero, rendered.TextPointer);
        }
        finally
        {
            _suppressInterception = false;
        }
        SendMessage(EM_EMPTYUNDOBUFFER, IntPtr.Zero, IntPtr.Zero);
    }

    internal bool IsInterceptionSuppressed => _suppressInterception;

    private void SendMessage(int msg, IntPtr wParam, IntPtr lParam) =>
        PasswordEyeToggleNative.SendMessage(_textBox.Handle, msg, wParam, lParam);

    private void ApplyRightMargin(int margin) =>
        PasswordEyeToggleNative.SendMessage(_textBox.Handle, EM_SETMARGINS,
            (IntPtr)EC_RIGHTMARGIN, (IntPtr)(margin << 16));
}
