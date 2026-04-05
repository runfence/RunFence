using System.Runtime.InteropServices;

namespace RunFence.DragBridge;

internal static class NativeInterop
{
    internal const int SWP_NOMOVE = 0x0002;
    internal const int SWP_NOSIZE = 0x0001;
    internal const int SWP_NOACTIVATE = 0x0010;
    internal static readonly nint HWND_TOPMOST = -1;

    [DllImport("user32.dll")]
    internal static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, int uFlags);

    [DllImport("user32.dll")]
    internal static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);
}