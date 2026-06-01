using System.Runtime.InteropServices;

namespace RunFence.AppxLauncher;

public static class ShellExecuteNative
{
    public const int ShowNormal = 1;
    public const int SuccessThreshold = 32;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr ShellExecuteW(
        IntPtr hwnd,
        string operation,
        string file,
        string? parameters,
        string? directory,
        int showCommand);
}
