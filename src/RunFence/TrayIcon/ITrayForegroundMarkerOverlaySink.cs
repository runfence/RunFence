using System.Drawing;

namespace RunFence.TrayIcon;

public interface ITrayForegroundMarkerOverlaySink
{
    void SetForegroundMarkerOverlay(Color? color);
}

