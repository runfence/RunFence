using System.Drawing;
using System.Windows.Forms;

namespace RunFence.ForegroundMarker;

public sealed class ForegroundMonitorIntersectionService : IForegroundMonitorIntersectionService
{
    public bool TryGetMonitorBounds(Rectangle bounds, out Rectangle monitorBounds)
    {
        if (Screen.AllScreens.Any(screen => screen.Bounds.IntersectsWith(bounds)))
        {
            monitorBounds = Screen.FromRectangle(bounds).Bounds;
            return true;
        }

        monitorBounds = Rectangle.Empty;
        return false;
    }
}
