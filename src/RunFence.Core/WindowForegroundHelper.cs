using System.Runtime.InteropServices;

namespace RunFence.Core;

/// <summary>
/// Forces a window to the foreground by temporarily attaching to the current foreground
/// thread's input queue, bypassing the foreground activation lock.
/// Also restores the window if it is minimized at the Win32 level — SetForegroundWindow
/// alone does not restore minimized windows, it only flashes the taskbar button.
/// </summary>
// P/Invoke duplication with WindowNative (RunFence project) is architecturally justified:
// RunFence.Core cannot reference the RunFence project, so window-management P/Invokes
// that are needed here must be declared locally.
public static class WindowForegroundHelper
{
    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hWnd, nint lpdwProcessId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(nint hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint hWnd, int nCmdShow);

    private const int SW_RESTORE = 9;

    public static void ForceToForeground(nint hWnd)
    {
        if (IsIconic(hWnd))
            ShowWindow(hWnd, SW_RESTORE);

        var foregroundHwnd = GetForegroundWindow();
        var foregroundThread = GetWindowThreadProcessId(foregroundHwnd, 0);
        var currentThread = GetCurrentThreadId();

        bool attached = foregroundThread != currentThread &&
                        AttachThreadInput(foregroundThread, currentThread, true);
        try
        {
            SetForegroundWindow(hWnd);
        }
        finally
        {
            if (attached)
                AttachThreadInput(foregroundThread, currentThread, false);
        }
    }
}