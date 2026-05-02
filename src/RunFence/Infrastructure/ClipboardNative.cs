using System.Runtime.InteropServices;

namespace RunFence.Infrastructure;

/// <summary>
/// P/Invoke declarations for clipboard and global-memory APIs.
/// Shared by <see cref="ClipboardPasteInterceptService"/> and <see cref="RunFence.Account.UI.SecurePasswordBox"/>.
/// </summary>
public static class ClipboardNative
{
    // ── User32 clipboard APIs ─────────────────────────────────────────────────

    [DllImport("user32.dll")]
    public static extern IntPtr GetClipboardOwner();

    [DllImport("user32.dll")]
    public static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll")]
    public static extern bool CloseClipboard();

    [DllImport("user32.dll")]
    public static extern bool EmptyClipboard();

    [DllImport("user32.dll")]
    public static extern uint EnumClipboardFormats(uint format);

    [DllImport("user32.dll")]
    public static extern IntPtr GetClipboardData(uint uFormat);

    [DllImport("user32.dll")]
    public static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

    // ── Kernel32 global memory APIs ───────────────────────────────────────────

    [DllImport("kernel32.dll")]
    public static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

    [DllImport("kernel32.dll")]
    public static extern IntPtr GlobalFree(IntPtr hMem);

    [DllImport("kernel32.dll")]
    public static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll")]
    public static extern bool GlobalUnlock(IntPtr hMem);

    [DllImport("kernel32.dll")]
    public static extern UIntPtr GlobalSize(IntPtr hMem);

    public const uint GMEM_MOVEABLE = 0x0002u;
}
