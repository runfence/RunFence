using System.Runtime.InteropServices;

namespace RunFence.Infrastructure;

/// <summary>
/// Shared P/Invoke declarations and helpers for shell icon retrieval via SHGetFileInfo.
/// Centralizes the SHFILEINFO struct, shell constants, and DestroyIcon usage that were
/// previously duplicated across IconService and AclPathIconProvider.
/// </summary>
public static class ShellIconHelper
{
    public const uint SHGFI_ICON = 0x100;
    public const uint SHGFI_SMALLICON = 0x1;
    public const uint SHGFI_LARGEICON = 0x0;
    public const uint SHGFI_LINKOVERLAY = 0x8000;
    public const uint SHGFI_USEFILEATTRIBUTES = 0x10;
    public const uint FILE_ATTRIBUTE_NORMAL = 0x80;
    public const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes,
        ref SHFILEINFO psfi, uint cbSizeFileInfo, uint uFlags);

    /// <summary>
    /// Returns a shell icon HICON for the given path and flags. The caller is responsible for
    /// destroying the returned HICON via <see cref="NativeMethods.DestroyIcon"/> when done.
    /// Returns <see cref="IntPtr.Zero"/> if the shell returns no icon.
    /// </summary>
    public static IntPtr GetFileIconHandle(string path, uint fileAttributes, uint flags)
    {
        var shInfo = new SHFILEINFO();
        var result = SHGetFileInfo(path, fileAttributes, ref shInfo, (uint)Marshal.SizeOf(shInfo), flags);
        if (result == IntPtr.Zero || shInfo.hIcon == IntPtr.Zero)
            return IntPtr.Zero;
        return shInfo.hIcon;
    }
}