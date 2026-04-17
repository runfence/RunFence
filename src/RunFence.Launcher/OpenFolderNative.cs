using System.Runtime.InteropServices;

namespace RunFence.Launcher;

internal static class OpenFolderNative
{
    public const int SHCNE_ASSOCCHANGED = 0x08000000;
    public const int SHCNF_IDLIST = 0x0000;

    [DllImport("shell32.dll")]
    public static extern void SHChangeNotify(int wEventId, int uFlags, IntPtr dwItem1, IntPtr dwItem2);
}
