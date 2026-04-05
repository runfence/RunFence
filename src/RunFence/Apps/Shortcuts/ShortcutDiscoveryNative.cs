using System.Runtime.InteropServices;

namespace RunFence.Apps.Shortcuts;

internal static class ShortcutDiscoveryNative
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern int ExtractIconEx(string szFileName, int nIconIndex,
        IntPtr[]? phiconLarge, IntPtr[]? phiconSmall, int nIcons);
}