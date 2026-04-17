using System.Runtime.InteropServices;
using RunFence.Launch;

namespace RunFence.Infrastructure;

public class ShellHelper(ILaunchFacade launchFacade)
{
    public void ShowProperties(string path, IWin32Window? owner = null)
    {
        var info = new ShellNative.ShellExecuteExInfo
        {
            cbSize = Marshal.SizeOf<ShellNative.ShellExecuteExInfo>(),
            fMask = 0xC, // SEE_MASK_INVOKEIDLIST
            hwnd = owner?.Handle ?? IntPtr.Zero,
            lpVerb = "properties",
            lpFile = path,
            nShow = 5 // SW_SHOW
        };
        ShellNative.ShellExecuteEx(ref info);
    }

    public void OpenInExplorer(string path)
    {
        launchFacade.LaunchFile("explorer.exe", AccountLaunchIdentity.CurrentAccountElevated, $"\"{path}\"")?.Dispose();
    }

    public void OpenDefaultAppsSettings()
    {
        launchFacade.LaunchUrl("ms-settings:defaultapps", AccountLaunchIdentity.CurrentAccountElevated);
    }
}
