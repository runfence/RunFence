using System.Drawing;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
namespace RunFence.ForegroundMarker;

public sealed class ForegroundMarkerWindow : IForegroundMarkerWindow
{
    private const int WmNcHitTest = 0x0084;
    private const int WmPaint = 0x000F;
    private const int HtTransparent = -1;
    private const int SwHide = 0;
    private const int WsPopup = unchecked((int)0x80000000u);
    private const int WsExLayered = 0x00080000;
    private const int WsExTransparent = 0x00000020;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;
    private const int WsExTopmost = 0x00000008;
    private const uint LwaAlpha = 0x00000002;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;
    private const string WindowClassName = "RunFenceForegroundMarkerWindow";

    private static readonly ConcurrentDictionary<IntPtr, ForegroundMarkerWindow> Windows = new();
    private static readonly ForegroundMarkerWindowNative.WndProc WindowProcedure = WndProc;
    private static readonly object RegisterClassLock = new();
    private static ushort windowClassAtom;

    private IntPtr handle;
    public const int MarkerWidth = 2;
    public IntPtr Handle => handle;

    private readonly IForegroundMarkerNativeMethods nativeMethods;
    private Rectangle markerBounds;
    private Color markerColor = ForegroundPrivilegeMarkerPalette.Basic;
    private bool disposed;

    public ForegroundMarkerWindow(IForegroundMarkerNativeMethods nativeMethods)
    {
        this.nativeMethods = nativeMethods;
    }

    public void Show(IntPtr targetWindow, Rectangle bounds, bool renderInsideLeftEdge, Color color)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        if (targetWindow == IntPtr.Zero || bounds.Width <= 0 || bounds.Height <= 0)
        {
            Hide();
            return;
        }

        EnsureHandleCreated();

        markerBounds = CalculateMarkerBounds(new ForegroundMarkerPlacement(bounds, renderInsideLeftEdge));
        markerColor = color;
        var insertAfter = ResolveInsertAfter(targetWindow);

        _ = nativeMethods.SetWindowPos(
            handle,
            insertAfter,
            markerBounds.X,
            markerBounds.Y,
            markerBounds.Width,
            markerBounds.Height,
            SwpNoActivate | SwpShowWindow);
        _ = nativeMethods.InvalidateRect(handle, IntPtr.Zero, erase: false);
    }

    public void Hide()
    {
        if (handle != IntPtr.Zero)
            _ = nativeMethods.ShowWindow(handle, SwHide);
    }

    public void Dispose()
    {
        if (disposed)
            return;

        Hide();
        if (handle != IntPtr.Zero)
        {
            _ = Windows.TryRemove(handle, out _);
            ForegroundMarkerWindowNative.DestroyWindow(handle);
            handle = IntPtr.Zero;
        }

        disposed = true;
    }

    public static Rectangle CalculateMarkerBounds(ForegroundMarkerPlacement placement)
    {
        var x = placement.RenderInsideLeftEdge
            ? placement.WindowBounds.Left
            : placement.WindowBounds.Left - MarkerWidth;
        return new Rectangle(x, placement.WindowBounds.Top, MarkerWidth, placement.WindowBounds.Height);
    }

    private IntPtr ResolveInsertAfter(IntPtr targetWindow)
    {
        var previousWindow = nativeMethods.GetPreviousWindow(targetWindow);
        while (previousWindow == handle)
            previousWindow = nativeMethods.GetPreviousWindow(previousWindow);

        if (previousWindow != IntPtr.Zero)
            return previousWindow;

        return nativeMethods.TryGetWindowExStyle(targetWindow, out var windowExStyle)
               && (windowExStyle & WsExTopmost) != 0
            ? nativeMethods.TopmostInsertAfter
            : nativeMethods.NotTopmostInsertAfter;
    }

    private void EnsureHandleCreated()
    {
        if (handle != IntPtr.Zero)
            return;

        var moduleHandle = ForegroundMarkerWindowNative.GetModuleHandle(null);
        RegisterWindowClass(moduleHandle);
        handle = ForegroundMarkerWindowNative.CreateWindowEx(
            WsExLayered | WsExTransparent | WsExNoActivate | WsExToolWindow | WsExTopmost,
            WindowClassName,
            string.Empty,
            WsPopup,
            -32000,
            -32000,
            MarkerWidth,
            1,
            IntPtr.Zero,
            IntPtr.Zero,
            moduleHandle,
            IntPtr.Zero);

        if (handle == IntPtr.Zero)
            throw new InvalidOperationException(
                $"Failed to create foreground marker window. Win32 error: {Marshal.GetLastWin32Error()}.");

        Windows[handle] = this;
        _ = nativeMethods.SetLayeredWindowAttributes(handle, 0, 255, LwaAlpha);
    }

    private static void RegisterWindowClass(IntPtr moduleHandle)
    {
        if (Volatile.Read(ref windowClassAtom) != 0)
            return;

        lock (RegisterClassLock)
        {
            if (windowClassAtom != 0)
                return;

            var windowClass = new ForegroundMarkerWindowNative.WndClassEx
            {
                Size = (uint)Marshal.SizeOf<ForegroundMarkerWindowNative.WndClassEx>(),
                WindowProcedure = WindowProcedure,
                Instance = moduleHandle,
                ClassName = WindowClassName
            };
            windowClassAtom = ForegroundMarkerWindowNative.RegisterClassEx(ref windowClass);
            if (windowClassAtom == 0)
                throw new InvalidOperationException(
                    $"Failed to register foreground marker window class. Win32 error: {Marshal.GetLastWin32Error()}.");
        }
    }

    private static IntPtr WndProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        switch (message)
        {
            case WmNcHitTest:
                return (IntPtr)HtTransparent;
            case WmPaint:
                if (Windows.TryGetValue(hwnd, out var window))
                {
                    window.PaintMarker(hwnd);
                    return IntPtr.Zero;
                }

                break;
        }

        return ForegroundMarkerWindowNative.DefWindowProc(hwnd, message, wParam, lParam);
    }

    private void PaintMarker(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return;

        var paintStruct = default(ForegroundMarkerWindowNative.PaintStruct);
        var hdc = ForegroundMarkerWindowNative.BeginPaint(hwnd, ref paintStruct);
        if (hdc == IntPtr.Zero)
            return;

        try
        {
            using var graphics = Graphics.FromHdc(hdc);
            using var brush = new SolidBrush(markerColor);
            graphics.FillRectangle(brush, 0, 0, markerBounds.Width, markerBounds.Height);
        }
        finally
        {
            ForegroundMarkerWindowNative.EndPaint(hwnd, ref paintStruct);
        }
    }
}
