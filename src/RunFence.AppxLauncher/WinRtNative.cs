using System.Runtime.InteropServices;

namespace RunFence.AppxLauncher;

public static class WinRtNative
{
    public const int RoInitSingleThreaded = 0;
    public const int SFalse = 1;
    public const int RpcEChangedMode = unchecked((int)0x80010106);
    public const uint QsAllInput = 0x04FF;
    public const uint MwmoInputAvailable = 0x0004;
    public const uint PmRemove = 0x0001;

    [DllImport("combase.dll")]
    public static extern int RoInitialize(int initType);

    [DllImport("combase.dll")]
    public static extern void RoUninitialize();

    [DllImport("combase.dll")]
    public static extern int RoGetActivationFactory(IntPtr activatableClassId, [In] ref Guid iid, out IntPtr factory);

    [DllImport("combase.dll")]
    public static extern int RoActivateInstance(IntPtr activatableClassId, out IntPtr instance);

    [DllImport("combase.dll", CharSet = CharSet.Unicode)]
    public static extern int WindowsCreateString(string sourceString, int length, out IntPtr handle);

    [DllImport("combase.dll")]
    public static extern int WindowsDeleteString(IntPtr handle);

    [DllImport("user32.dll")]
    public static extern uint MsgWaitForMultipleObjectsEx(
        uint nCount,
        IntPtr pHandles,
        uint dwMilliseconds,
        uint dwWakeMask,
        uint dwFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PeekMessage(out Msg lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

    [DllImport("user32.dll")]
    public static extern bool TranslateMessage([In] ref Msg lpMsg);

    [DllImport("user32.dll")]
    public static extern IntPtr DispatchMessage([In] ref Msg lpMsg);

    [StructLayout(LayoutKind.Sequential)]
    public struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Msg
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public Point pt;
        public uint lPrivate;
    }
}
