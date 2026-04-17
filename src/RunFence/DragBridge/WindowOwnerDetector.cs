using System.Runtime.InteropServices;
using RunFence.Core;
using RunFence.Infrastructure;
using InfraWindowNative = RunFence.Infrastructure.WindowNative;

namespace RunFence.DragBridge;

/// <summary>
/// Detects the owner SID and integrity level of the foreground window or the drag source (mouse-capture) window.
/// Mouse capture is a best-effort heuristic — capture can also mean button hold, scrollbar,
/// window resize, etc. When wrong, the user simply gets a DragBridge for the wrong account;
/// they can click to cancel and retry.
/// </summary>
public class WindowOwnerDetector : IWindowOwnerDetector
{
    public WindowOwnerInfo? GetForegroundWindowOwnerInfo()
    {
        var hwnd = InfraWindowNative.GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
            return null;
        InfraWindowNative.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == 0)
            return null;
        return TryGetOwnerInfo(pid);
    }

    public WindowOwnerInfo? GetDragSourceOrForegroundOwnerInfo()
    {
        var hwnd = InfraWindowNative.GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
            return TryGetWindowAtCursorInfo();

        var threadId = InfraWindowNative.GetWindowThreadProcessId(hwnd, out var fgPid);

        var info = new InfraWindowNative.GUITHREADINFO { cbSize = Marshal.SizeOf<InfraWindowNative.GUITHREADINFO>() };
        if (InfraWindowNative.GetGUIThreadInfo(threadId, ref info)
            && info.hwndCapture != IntPtr.Zero
            && info.hwndCapture != hwnd)
        {
            // Another window holds mouse capture — heuristic: likely the drag source
            InfraWindowNative.GetWindowThreadProcessId(info.hwndCapture, out var capturePid);
            if (capturePid != 0)
            {
                var captureInfo = TryGetOwnerInfo(capturePid);
                if (captureInfo != null)
                    return captureInfo;
            }
        }

        if (fgPid != 0)
        {
            var fgInfo = TryGetOwnerInfo(fgPid);
            if (fgInfo != null)
                return fgInfo;
        }

        // Third fallback: window under cursor — reliable when the hotkey is pressed while cursor
        // is over the source window (e.g. FolderBrowser popup, tool windows without activation).
        return TryGetWindowAtCursorInfo();
    }

    private static WindowOwnerInfo? TryGetOwnerInfo(uint pid)
    {
        var sid = NativeTokenHelper.TryGetProcessOwnerSid(pid);
        if (sid == null)
            return null;
        var il = NativeTokenHelper.TryGetProcessIntegrityLevel(pid) ?? NativeTokenHelper.MandatoryLevelMedium;
        return new WindowOwnerInfo(sid, il);
    }

    private static WindowOwnerInfo? TryGetWindowAtCursorInfo()
    {
        if (!InfraWindowNative.GetCursorPos(out var pt))
            return null;
        var hwndAtCursor = InfraWindowNative.WindowFromPoint(pt);
        if (hwndAtCursor == IntPtr.Zero)
            return null;
        InfraWindowNative.GetWindowThreadProcessId(hwndAtCursor, out var pid);
        if (pid == 0)
            return null;
        return TryGetOwnerInfo(pid);
    }
}