namespace RunFence.Account.UI;

internal sealed class SecurePasswordWndProcInterceptor : NativeWindow
{
    private const int WM_CHAR = 0x0102;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_PASTE = 0x0302;
    private const int WM_COPY = 0x0301;
    private const int WM_CUT = 0x0300;
    private const int WM_CLEAR = 0x0303;
    private const int WM_UNDO = 0x0304;
    private const int VK_DELETE = 0x2E;

    private readonly SecurePasswordBox _owner;
    private readonly TextBox _textBox;

    public SecurePasswordWndProcInterceptor(SecurePasswordBox owner, TextBox textBox)
    {
        _owner = owner;
        _textBox = textBox;

        if (_textBox.IsHandleCreated)
            AssignHandle(_textBox.Handle);
        else
            _textBox.HandleCreated += (_, _) => AssignHandle(_textBox.Handle);
    }

    protected override void WndProc(ref Message m)
    {
        if (_owner.IsInterceptionSuppressed)
        {
            base.WndProc(ref m);
            return;
        }

        switch (m.Msg)
        {
            case WM_CHAR:
                HandleChar(ref m);
                return;

            case WM_KEYDOWN:
                if (HandleKeyDown(ref m))
                    return;
                return;

            case WM_PASTE:
                _owner.ApplyEditResult(_owner.HandlePaste());
                return;

            case WM_COPY:
                _owner.ApplyEditResult(_owner.HandleCopy());
                return;

            case WM_CUT:
                _owner.ApplyEditResult(_owner.HandleCut());
                return;

            case WM_CLEAR:
                _owner.ApplyEditResult(_owner.HandleClear());
                return;

            case WM_UNDO:
                _owner.ApplyEditResult(_owner.HandleUndo());
                return;

            default:
                base.WndProc(ref m);
                return;
        }
    }

    private void HandleChar(ref Message m)
    {
        char ch = (char)(int)m.WParam;
        _owner.ApplyEditResult(_owner.HandleChar(ch));

        if (ch != '\b' && ch != (char)0x7F && char.IsControl(ch))
            base.WndProc(ref m);
    }

    private bool HandleKeyDown(ref Message m)
    {
        var keyCode = (Keys)(int)m.WParam;
        var modifiers = Control.ModifierKeys & (Keys.Control | Keys.Shift | Keys.Alt);
        if (_owner.HandleShortcutKey(keyCode, modifiers))
            return true;

        if (keyCode == (Keys)VK_DELETE && (modifiers & Keys.Control) == 0)
        {
            _owner.ApplyEditResult(_owner.HandleDelete());
            return true;
        }

        base.WndProc(ref m);
        return true;
    }
}
