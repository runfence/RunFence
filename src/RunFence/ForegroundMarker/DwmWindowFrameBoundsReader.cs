using System.Drawing;
using System.Runtime.InteropServices;
using RunFence.Infrastructure;

namespace RunFence.ForegroundMarker;

public sealed class DwmWindowFrameBoundsReader : IWindowFrameBoundsReader
{
    private const int DwmwaExtendedFrameBounds = 9;

    public bool TryGetExtendedFrameBounds(IntPtr hwnd, out Rectangle bounds)
    {
        bounds = Rectangle.Empty;
        if (DwmGetWindowAttribute(
                hwnd,
                DwmwaExtendedFrameBounds,
                out WindowNative.RECT rect,
                Marshal.SizeOf<WindowNative.RECT>()) != 0)
            return false;

        bounds = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
        return true;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(
        IntPtr hwnd,
        int dwAttribute,
        out WindowNative.RECT pvAttribute,
        int cbAttribute);
}
