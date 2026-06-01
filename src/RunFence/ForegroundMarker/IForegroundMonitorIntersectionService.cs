using System.Drawing;

namespace RunFence.ForegroundMarker;

public interface IForegroundMonitorIntersectionService
{
    bool TryGetMonitorBounds(Rectangle bounds, out Rectangle monitorBounds);
}
