using System.Runtime.InteropServices;
using RunFence.Infrastructure;
using InfraWindowNative = RunFence.Infrastructure.WindowNative;

namespace RunFence.DragBridge;

public sealed class WindowOwnerNativeReader : IWindowOwnerNativeReader
{
    public bool TryGetForegroundWindow(out IntPtr hwnd, out uint threadId, out uint processId)
    {
        hwnd = InfraWindowNative.GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
        {
            threadId = 0;
            processId = 0;
            return false;
        }

        threadId = InfraWindowNative.GetWindowThreadProcessId(hwnd, out processId);
        return true;
    }

    public bool TryGetCaptureWindowProcessId(uint foregroundThreadId, IntPtr foregroundHwnd, out uint processId)
    {
        processId = 0;
        if (foregroundThreadId == 0 || foregroundHwnd == IntPtr.Zero)
            return false;

        var info = new InfraWindowNative.GUITHREADINFO { cbSize = Marshal.SizeOf<InfraWindowNative.GUITHREADINFO>() };
        if (!InfraWindowNative.GetGUIThreadInfo(foregroundThreadId, ref info)
            || info.hwndCapture == IntPtr.Zero
            || info.hwndCapture == foregroundHwnd)
        {
            return false;
        }

        InfraWindowNative.GetWindowThreadProcessId(info.hwndCapture, out processId);
        return processId != 0;
    }

    public bool TryGetCursorWindowProcessId(out uint processId)
    {
        processId = 0;
        if (!InfraWindowNative.GetCursorPos(out var pt))
            return false;

        var hwndAtCursor = InfraWindowNative.WindowFromPoint(pt);
        if (hwndAtCursor == IntPtr.Zero)
            return false;

        InfraWindowNative.GetWindowThreadProcessId(hwndAtCursor, out processId);
        return processId != 0;
    }
}
