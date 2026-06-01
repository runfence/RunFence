using System.Runtime.InteropServices;

namespace RunFence.TrayIcon;

public static class TrayIconOverlayNative
{
    [DllImport("user32.dll")]
    public static extern bool DestroyIcon(IntPtr hIcon);
}

