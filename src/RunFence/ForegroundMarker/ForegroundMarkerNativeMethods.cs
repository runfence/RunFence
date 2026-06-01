using System.Drawing;
using System.Runtime.InteropServices;
using RunFence.Infrastructure;

namespace RunFence.ForegroundMarker;

public interface IForegroundMarkerNativeMethods
{
    IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);
    IntPtr GetPreviousWindow(IntPtr hwnd);
    IntPtr TopmostInsertAfter { get; }
    IntPtr NotTopmostInsertAfter { get; }
    bool IsWindow(IntPtr hwnd);
    bool IsIconic(IntPtr hwnd);
    bool TryGetWindowStyle(IntPtr hwnd, out long windowStyle);
    bool TryGetWindowExStyle(IntPtr hwnd, out long windowExStyle);
    bool TryGetWindowRect(IntPtr hwnd, out Rectangle bounds);
    bool TryGetWindowCloaked(IntPtr hwnd, out bool isCloaked);
    bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
    bool ShowWindow(IntPtr hwnd, int command);
    bool InvalidateRect(IntPtr hwnd, IntPtr rect, bool erase);
    bool SetLayeredWindowAttributes(IntPtr hwnd, uint colorKey, byte alpha, uint flags);
}

internal sealed class ForegroundMarkerNativeMethods : IForegroundMarkerNativeMethods
{
    private const int DwmwaCloaked = 14;
    private const int GwlStyle = -16;
    private const int GwlExStyle = -20;
    private static readonly IntPtr HwndTopmost = new(-1);
    private static readonly IntPtr HwndNotTopmost = new(-2);

    public IntPtr GetAncestor(IntPtr hwnd, uint gaFlags) => WindowNative.GetAncestor(hwnd, gaFlags);

    public IntPtr GetPreviousWindow(IntPtr hwnd) => WindowNative.GetWindow(hwnd, WindowNative.GW_HWNDPREV);

    public IntPtr TopmostInsertAfter => HwndTopmost;

    public IntPtr NotTopmostInsertAfter => HwndNotTopmost;

    public bool IsWindow(IntPtr hwnd) => WindowNative.IsWindow(hwnd);

    public bool IsIconic(IntPtr hwnd) => WindowNative.IsIconic(hwnd);

    public bool TryGetWindowStyle(IntPtr hwnd, out long windowStyle)
        => TryGetWindowLong(hwnd, GwlStyle, out windowStyle);

    public bool TryGetWindowExStyle(IntPtr hwnd, out long windowExStyle)
        => TryGetWindowLong(hwnd, GwlExStyle, out windowExStyle);

    private bool TryGetWindowLong(IntPtr hwnd, int index, out long windowLong)
    {
        windowLong = 0;
        if (!IsWindow(hwnd))
            return false;

        Marshal.SetLastPInvokeError(0);
        var style = GetWindowLongPtr(hwnd, index);
        if (style == IntPtr.Zero && Marshal.GetLastPInvokeError() != 0)
            return false;

        windowLong = style.ToInt64();
        return true;
    }

    public bool TryGetWindowRect(IntPtr hwnd, out Rectangle bounds)
    {
        bounds = Rectangle.Empty;
        if (!GetWindowRect(hwnd, out var rect))
            return false;

        bounds = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
        return true;
    }

    public bool TryGetWindowCloaked(IntPtr hwnd, out bool isCloaked)
    {
        isCloaked = false;
        if (DwmGetWindowAttribute(hwnd, DwmwaCloaked, out int cloaked, Marshal.SizeOf<int>()) != 0)
            return false;

        isCloaked = cloaked != 0;
        return true;
    }

    public bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags)
        => SetWindowPosNative(hwnd, insertAfter, x, y, cx, cy, flags);

    public bool ShowWindow(IntPtr hwnd, int command) => WindowNative.ShowWindow(hwnd, command);

    public bool InvalidateRect(IntPtr hwnd, IntPtr rect, bool erase) => InvalidateRectNative(hwnd, rect, erase);

    public bool SetLayeredWindowAttributes(IntPtr hwnd, uint colorKey, byte alpha, uint flags)
        => SetLayeredWindowAttributesNative(hwnd, colorKey, alpha, flags);

    [DllImport("user32.dll", EntryPoint = "GetWindowRect")]
    private static extern bool GetWindowRect(IntPtr hwnd, out WindowNative.RECT rect);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowPos", SetLastError = true)]
    private static extern bool SetWindowPosNative(
        IntPtr hwnd,
        IntPtr insertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint flags);

    [DllImport("user32.dll", EntryPoint = "InvalidateRect", SetLastError = true)]
    private static extern bool InvalidateRectNative(IntPtr hwnd, IntPtr rect, bool erase);

    [DllImport("user32.dll", EntryPoint = "SetLayeredWindowAttributes", SetLastError = true)]
    private static extern bool SetLayeredWindowAttributesNative(IntPtr hwnd, uint colorKey, byte alpha, uint flags);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(
        IntPtr hwnd,
        int dwAttribute,
        out int pvAttribute,
        int cbAttribute);
}
