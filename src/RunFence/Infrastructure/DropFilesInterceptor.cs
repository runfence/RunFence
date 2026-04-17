namespace RunFence.Infrastructure;

/// <summary>
/// Attaches to a control HWND to receive WM_DROPFILES messages from shell file drops,
/// including cross-IL drops from unelevated Explorer. Works because:
/// <list type="bullet">
/// <item>The target control has <c>AllowDrop = false</c> (no RegisterDragDrop), so Explorer
/// uses the old-style WM_DROPFILES path instead of OLE IDropTarget (which cannot transfer
/// data across IL boundaries).</item>
/// <item><see cref="WindowSecurityService"/> applies <c>ChangeWindowMessageFilterEx</c> for
/// WM_DROPFILES on every HWND in the process, allowing the message from lower-IL senders.</item>
/// <item><see cref="ShellNative.DragAcceptFiles"/> sets WS_EX_ACCEPTFILES so the shell
/// knows this window accepts file drops.</item>
/// </list>
/// </summary>
public sealed class DropFilesInterceptor : NativeWindow, IDisposable
{
    private readonly Action<string[]> _onDrop;

    public DropFilesInterceptor(IntPtr hwnd, Action<string[]> onDrop)
    {
        _onDrop = onDrop;
        ShellNative.DragAcceptFiles(hwnd, true);
        AssignHandle(hwnd);
    }

    public void Dispose() => ReleaseHandle();

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WindowNative.WM_DROPFILES)
        {
            var paths = ShellNative.ExtractDropPaths(m.WParam);
            ShellNative.DragFinish(m.WParam);
            if (paths.Length > 0)
                _onDrop(paths);
            return;
        }

        base.WndProc(ref m);
    }
}