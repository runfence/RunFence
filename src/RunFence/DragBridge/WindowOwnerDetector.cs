using RunFence.Core;
using RunFence.Infrastructure;

namespace RunFence.DragBridge;

/// <summary>
/// Detects the owner SID and integrity level of the foreground window or the drag source (mouse-capture) window.
/// Mouse capture is a best-effort heuristic - capture can also mean button hold, scrollbar,
/// window resize, etc. When wrong, the user simply gets a DragBridge for the wrong account;
/// they can click to cancel and retry.
/// </summary>
public class WindowOwnerDetector(
    IRestrictedJobInspector restrictedJobInspector,
    IWindowOwnerNativeReader nativeReader,
    IWindowOwnerProcessTokenReader processTokenReader) : IWindowOwnerDetector
{
    public WindowOwnerInfo? GetForegroundWindowOwnerInfo()
    {
        if (!nativeReader.TryGetForegroundWindow(out _, out _, out var pid) || pid == 0)
            return null;

        return TryGetOwnerInfo(pid);
    }

    public WindowOwnerInfo? GetDragSourceOrForegroundOwnerInfo()
    {
        if (!nativeReader.TryGetForegroundWindow(out var hwnd, out var threadId, out var fgPid))
            return TryGetWindowAtCursorInfo();

        if (nativeReader.TryGetCaptureWindowProcessId(threadId, hwnd, out var capturePid)
            && capturePid != 0)
        {
            var captureInfo = TryGetOwnerInfo(capturePid);
            if (captureInfo != null)
                return captureInfo;
        }

        if (fgPid != 0)
        {
            var fgInfo = TryGetOwnerInfo(fgPid);
            if (fgInfo != null)
                return fgInfo;
        }

        return TryGetWindowAtCursorInfo();
    }

    private WindowOwnerInfo? TryGetOwnerInfo(uint pid)
    {
        if (!processTokenReader.TryGetTokenInfo(pid, out var tokenInfo))
            return null;

        var il = tokenInfo.IntegrityLevel ?? NativeTokenHelper.MandatoryLevelMedium;
        var inRestrictedJob = restrictedJobInspector.IsProcessInHandleLimitedJob((int)pid);
        return new WindowOwnerInfo(tokenInfo.OwnerSid, il, inRestrictedJob, tokenInfo.AppContainerSid, tokenInfo.IsElevated);
    }

    private WindowOwnerInfo? TryGetWindowAtCursorInfo()
    {
        if (!nativeReader.TryGetCursorWindowProcessId(out var pid) || pid == 0)
            return null;

        return TryGetOwnerInfo(pid);
    }
}
