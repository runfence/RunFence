using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace RunFence.Infrastructure;

/// <summary>
/// P/Invoke declarations for shell execution, file association notifications,
/// and shell drag-drop APIs. Consumed by launch, handler registration, and folder services.
/// </summary>
public static class ShellNative
{
    // ── Shell32 ────────────────────────────────────────────────────────────────

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr ShellExecute(IntPtr hwnd, string? lpVerb, string lpFile,
        string? lpParameters, string? lpDirectory, int nShowCmd);

    [DllImport("shell32.dll")]
    public static extern void SHChangeNotify(int wEventId, int uFlags, IntPtr dwItem1, IntPtr dwItem2);

    public const int SHCNE_ASSOCCHANGED = 0x08000000;
    public const int SHCNF_IDLIST = 0x0000;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct ShellExecuteExInfo
    {
        public int cbSize;
        public uint fMask;
        public IntPtr hwnd;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpVerb;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpFile;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpParameters;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpDirectory;
        public int nShow;
        public IntPtr hInstApp;
        public IntPtr lpIDList;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpClass;
        public IntPtr hkeyClass;
        public uint dwHotKey;
        public IntPtr hIconOrMonitor;
        public IntPtr hProcess;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool ShellExecuteEx(ref ShellExecuteExInfo pExecInfo);

    // ── Shell drag-drop (WM_DROPFILES) ────────────────────────────────────────

    [DllImport("shell32.dll")]
    public static extern void DragAcceptFiles(IntPtr hWnd, bool fAccept);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern uint DragQueryFile(IntPtr hDrop, uint iFile, [Out] StringBuilder? lpszFile, uint cch);

    [DllImport("shell32.dll")]
    public static extern void DragFinish(IntPtr hDrop);

    /// <summary>Extracts all file paths from a WM_DROPFILES HDROP handle.</summary>
    public static string[] ExtractDropPaths(IntPtr hDrop)
    {
        uint count = DragQueryFile(hDrop, 0xFFFFFFFF, null, 0);
        var paths = new string[count];
        for (uint i = 0; i < count; i++)
        {
            uint len = DragQueryFile(hDrop, i, null, 0);
            var sb = new StringBuilder((int)(len + 1));
            DragQueryFile(hDrop, i, sb, len + 1);
            paths[i] = sb.ToString();
        }

        return paths;
    }

    // ── Shell folder open ─────────────────────────────────────────────────────

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern int SHParseDisplayName(string pszName, IntPtr pbc, out IntPtr ppidl,
        uint sfgaoIn, out uint psfgaoOut);

    // SEE_MASK_IDLIST: use lpIDList instead of lpFile; SEE_MASK_FLAG_NO_UI: suppress error dialogs
    public const uint SeeMaskIdList = 0x00000004;
    public const uint SeeMaskFlagNoUi = 0x00000400;
    public const int SwShownormal = 1;

    [DllImport("ole32.dll")]
    public static extern void CoTaskMemFree(IntPtr pv);

    // ── Shell icon (SHGetFileInfo) ─────────────────────────────────────────────

    public const uint SHGFI_ICON = 0x100;
    public const uint SHGFI_SMALLICON = 0x1;
    public const uint SHGFI_LARGEICON = 0x0;
    public const uint SHGFI_LINKOVERLAY = 0x8000;
    public const uint SHGFI_USEFILEATTRIBUTES = 0x10;

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
    /// destroying the returned HICON via <see cref="WindowNative.DestroyIcon"/> when done.
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
