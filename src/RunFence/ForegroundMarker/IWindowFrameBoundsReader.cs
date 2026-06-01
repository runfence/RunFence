using System.Drawing;

namespace RunFence.ForegroundMarker;

public interface IWindowFrameBoundsReader
{
    bool TryGetExtendedFrameBounds(IntPtr hwnd, out Rectangle bounds);
}
