using System.Runtime.InteropServices;

namespace RunFence.Launcher;

internal static class LauncherNative
{
    /// <summary>Pass to <see cref="AllowSetForegroundWindow"/> to grant any process foreground rights.</summary>
    public const uint ASFW_ANY = 0xFFFFFFFF;

    [DllImport("user32.dll")]
    public static extern bool AllowSetForegroundWindow(uint dwProcessId);
}