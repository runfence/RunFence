using System.Runtime.InteropServices;

namespace RunFence.ForegroundMarker;

internal static class ForegroundMarkerThreadDispatcherNative
{
    public const uint WmApp = 0x8000;
    public const uint PmNoRemove = 0x0000;

    [StructLayout(LayoutKind.Sequential)]
    public struct Msg
    {
        public IntPtr HWnd;
        public uint Message;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public Point Point;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Point
    {
        public int X;
        public int Y;
    }

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    public static extern bool PeekMessage(out Msg message, IntPtr hWnd, uint filterMin, uint filterMax, uint removeMessage);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool PostThreadMessage(uint threadId, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern int GetMessage(out Msg message, IntPtr hWnd, uint filterMin, uint filterMax);

    [DllImport("user32.dll")]
    public static extern bool TranslateMessage(ref Msg message);

    [DllImport("user32.dll")]
    public static extern IntPtr DispatchMessage(ref Msg message);

    [DllImport("user32.dll")]
    public static extern void PostQuitMessage(int exitCode);
}
