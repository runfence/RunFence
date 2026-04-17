using System.Runtime.InteropServices;

namespace RunFence.DragBridge;

public static class WindowNative
{
    public const int SWP_NOMOVE = 0x0002;
    public const int SWP_NOSIZE = 0x0001;
    public const int SWP_NOACTIVATE = 0x0010;
    public static readonly nint HWND_TOPMOST = -1;

    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, int uFlags);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);
}