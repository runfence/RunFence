using System.Runtime.InteropServices;

namespace RunFence.Core;

/// <summary>
/// Forces a window to the foreground by temporarily attaching to the current foreground
/// thread's input queue, bypassing the foreground activation lock.
/// </summary>
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

    public static void ForceToForeground(nint hWnd)
    {
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