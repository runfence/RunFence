using System.Drawing;
using RunFence.Infrastructure;

namespace RunFence.ForegroundMarker;

public sealed class ForegroundWindowBoundsReader : IForegroundWindowBoundsReader
{
    private const long WsCaption = 0x00C00000L;
    private const long WsThickFrame = 0x00040000L;
    private const long FullscreenBlockingFrameMask = WsCaption | WsThickFrame;

    private readonly IWindowFrameBoundsReader windowFrameBoundsReader;
    private readonly IForegroundMarkerNativeMethods nativeMethods;
    private readonly IForegroundMonitorIntersectionService monitorIntersectionService;

    public ForegroundWindowBoundsReader(
        IWindowFrameBoundsReader windowFrameBoundsReader,
        IForegroundMarkerNativeMethods nativeMethods,
        IForegroundMonitorIntersectionService monitorIntersectionService)
    {
        this.windowFrameBoundsReader = windowFrameBoundsReader;
        this.nativeMethods = nativeMethods;
        this.monitorIntersectionService = monitorIntersectionService;
    }

    public IntPtr ResolveTrackedTopLevelWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !nativeMethods.IsWindow(hwnd))
            return IntPtr.Zero;

        var root = nativeMethods.GetAncestor(hwnd, WindowNative.GA_ROOT);
        return root != IntPtr.Zero ? root : hwnd;
    }

    public bool TryGetVisibleBounds(IntPtr hwnd, out Rectangle bounds)
    {
        bounds = Rectangle.Empty;
        var trackedWindow = ResolveTrackedTopLevelWindow(hwnd);
        if (trackedWindow == IntPtr.Zero || nativeMethods.IsIconic(trackedWindow))
            return false;

        if (nativeMethods.TryGetWindowCloaked(trackedWindow, out var isCloaked) && isCloaked)
            return false;

        if (!TryReadVisibleBounds(trackedWindow, out bounds))
            return false;

        if (bounds.Width <= 0 || bounds.Height <= 0)
            return false;

        return monitorIntersectionService.TryGetMonitorBounds(bounds, out _);
    }

    public bool ShouldRenderInsideLeftEdge(Rectangle bounds)
    {
        return !monitorIntersectionService.TryGetMonitorBounds(bounds, out var monitorBounds)
               || bounds.Left - monitorBounds.Left <= ForegroundMarkerWindow.MarkerWidth;
    }

    public bool IsFullscreen(IntPtr hwnd, Rectangle bounds)
    {
        var trackedWindow = ResolveTrackedTopLevelWindow(hwnd);
        if (trackedWindow == IntPtr.Zero)
            return false;

        if (!monitorIntersectionService.TryGetMonitorBounds(bounds, out var monitorBounds)
            || Rectangle.Intersect(bounds, monitorBounds) != monitorBounds)
        {
            return false;
        }

        return nativeMethods.TryGetWindowStyle(trackedWindow, out var windowStyle)
               && (windowStyle & FullscreenBlockingFrameMask) == 0;
    }

    private bool TryReadVisibleBounds(IntPtr hwnd, out Rectangle bounds)
    {
        if (windowFrameBoundsReader.TryGetExtendedFrameBounds(hwnd, out bounds))
            return true;

        return nativeMethods.TryGetWindowRect(hwnd, out bounds);
    }
}
