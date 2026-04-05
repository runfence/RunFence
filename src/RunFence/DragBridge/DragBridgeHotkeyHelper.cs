namespace RunFence.DragBridge;

/// <summary>
/// Converts WinForms <see cref="System.Windows.Forms.Keys"/> modifier flags to
/// Win32 RegisterHotKey MOD_* constants and a virtual-key code.
/// </summary>
public static class DragBridgeHotkeyHelper
{
    // MOD_* flags for RegisterHotKey (not the same bit values as Keys modifier flags)
    private const int ModAlt = 0x0001;
    private const int ModControl = 0x0002;
    private const int ModShift = 0x0004;
    private const int ModWin = 0x0008;

    // 0x80000 is our custom storage bit for the Win modifier (no standard WinForms Keys bit exists for it)
    public const int WinModifierBit = 0x80000;

    public static int SplitModifiers(int keysValue)
    {
        int mods = 0;
        if ((keysValue & 0x20000) != 0)
            mods |= ModControl;
        if ((keysValue & 0x40000) != 0)
            mods |= ModAlt;
        if ((keysValue & 0x10000) != 0)
            mods |= ModShift;
        if ((keysValue & WinModifierBit) != 0)
            mods |= ModWin;
        return mods;
    }

    public static int GetVirtualKey(int keysValue) => keysValue & 0xFFFF;

    /// <summary>Returns true if the stored value has at least one modifier (Ctrl/Alt/Shift/Win).</summary>
    public static bool HasModifier(int keysValue)
        => (keysValue & (0x10000 | 0x20000 | 0x40000 | WinModifierBit)) != 0;
}