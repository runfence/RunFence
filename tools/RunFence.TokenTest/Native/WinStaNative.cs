using System.Runtime.InteropServices;

namespace RunFence.TokenTest.Native;

internal static class WinStaNative
{
    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr GetProcessWindowStation();

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr OpenDesktop(
        string lpszDesktop,
        uint dwFlags,
        bool fInherit,
        uint dwDesiredAccess);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool CloseDesktop(IntPtr hDesktop);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr OpenWindowStation(string lpszWinSta, bool fInherit, uint dwDesiredAccess);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool CloseWindowStation(IntPtr hWinSta);
}
