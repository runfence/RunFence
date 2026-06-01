using System;

namespace RunFence.Apps.Shortcuts;

public sealed class ShortcutIconNativeApi : IShortcutIconNativeApi
{
    public int ExtractIconEx(
        string szFileName,
        int nIconIndex,
        IntPtr[]? phiconLarge,
        IntPtr[]? phiconSmall,
        int nIcons)
    {
        return ShortcutDiscoveryNative.ExtractIconEx(szFileName, nIconIndex, phiconLarge, phiconSmall, nIcons);
    }
}

