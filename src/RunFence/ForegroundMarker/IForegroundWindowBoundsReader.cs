using System.Drawing;

namespace RunFence.ForegroundMarker;

public interface IForegroundWindowBoundsReader
{
    IntPtr ResolveTrackedTopLevelWindow(IntPtr hwnd);
    bool TryGetVisibleBounds(IntPtr hwnd, out Rectangle bounds);
    bool IsFullscreen(IntPtr hwnd, Rectangle bounds);
    bool ShouldRenderInsideLeftEdge(Rectangle bounds);
}
