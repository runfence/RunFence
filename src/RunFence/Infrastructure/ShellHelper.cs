using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RunFence.Infrastructure;

/// <summary>
/// Shared shell utility methods that wrap ShellExecuteEx P/Invoke operations.
/// </summary>
public static class ShellHelper
{
    public static void ShowProperties(string path, IWin32Window? owner = null)
    {
        var info = new NativeMethods.ShellExecuteExInfo
        {
            cbSize = Marshal.SizeOf<NativeMethods.ShellExecuteExInfo>(),
            fMask = 0xC, // SEE_MASK_INVOKEIDLIST
            hwnd = owner?.Handle ?? IntPtr.Zero,
            lpVerb = "properties",
            lpFile = path,
            nShow = 5 // SW_SHOW
        };
        NativeMethods.ShellExecuteEx(ref info);
    }

    public static void OpenInExplorer(string path)
    {
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
    }

    public static void OpenDefaultAppsSettings()
    {
        Process.Start(new ProcessStartInfo("ms-settings:defaultapps") { UseShellExecute = true });
    }
}