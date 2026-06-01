using System.Drawing;

namespace RunFence.ForegroundMarker;

public interface IForegroundMarkerWindow : IDisposable
{
    void Show(IntPtr targetWindow, Rectangle bounds, bool renderInsideLeftEdge, Color color);
    void Hide();
}
