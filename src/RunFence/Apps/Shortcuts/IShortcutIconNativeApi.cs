using System;

namespace RunFence.Apps.Shortcuts;

public interface IShortcutIconNativeApi
{
    int ExtractIconEx(
        string szFileName,
        int nIconIndex,
        IntPtr[]? phiconLarge,
        IntPtr[]? phiconSmall,
        int nIcons);
}

