using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Security.Principal;
using System.Text;

namespace RunFence.Infrastructure;

/// <summary>
/// Consolidated P/Invoke declarations shared across multiple services to avoid duplication.
/// Contains only those declarations that appear in two or more service files.
/// </summary>
public static class NativeMethods
{
    // ── Kernel32 ───────────────────────────────────────────────────────────────

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll")]
    public static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr LocalFree(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool QueryFullProcessImageName(IntPtr hProcess, uint dwFlags,
        StringBuilder lpExeName, ref uint lpdwSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress,
        IntPtr lpBuffer, IntPtr nSize, out IntPtr lpNumberOfBytesRead);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool IsWow64Process(IntPtr hProcess, out bool wow64Process);

    // ── Ntdll ──────────────────────────────────────────────────────────────────

    [DllImport("ntdll.dll")]
    public static extern uint NtQueryInformationProcess(IntPtr ProcessHandle,
        uint ProcessInformationClass, ref ProcessBasicInformation ProcessInformation,
        uint ProcessInformationLength, out uint ReturnLength);

    [StructLayout(LayoutKind.Sequential)]
    public struct ProcessBasicInformation
    {
        public IntPtr Reserved1;
        public IntPtr PebBaseAddress;
        public IntPtr Reserved2;
        public IntPtr Reserved3;
        public IntPtr UniqueProcessId;
        public IntPtr Reserved4;
    }

    // ── Advapi32 ───────────────────────────────────────────────────────────────

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool OpenProcessToken(IntPtr ProcessHandle,
        uint DesiredAccess, out IntPtr TokenHandle);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool ConvertStringSidToSid(string StringSid, out IntPtr Sid);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool GetTokenInformation(IntPtr TokenHandle, int TokenInformationClass,
        IntPtr TokenInformation, uint TokenInformationLength, out uint ReturnLength);

    // ── User32 (icon management) ──────────────────────────────────────────────

    [DllImport("user32.dll")]
    public static extern bool DestroyIcon(IntPtr hIcon);

    // ── User32 (UIPI message filter) ──────────────────────────────────────────

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool ChangeWindowMessageFilterEx(
        IntPtr hWnd, uint msg, uint action, IntPtr changeInfo);

    public const uint MSGFLT_ALLOW = 1;
    public const uint MSGFLT_DISALLOW = 2;
    public const uint WM_COPYGLOBALDATA = 0x0049; // undocumented OLE internal
    public const uint WM_COPYDATA = 0x004A;
    public const uint WM_DROPFILES = 0x0233;
    public const uint WM_GETOBJECT = 0x003D;

    /// <summary>
    /// Allows OLE drag-and-drop messages from lower-IL processes to reach the given HWND.
    /// Must be called after the control handle is created.
    /// </summary>
    public static void AllowDropFromLowIL(IntPtr hwnd)
    {
        ChangeWindowMessageFilterEx(hwnd, WM_DROPFILES, MSGFLT_ALLOW, IntPtr.Zero);
        ChangeWindowMessageFilterEx(hwnd, WM_COPYDATA, MSGFLT_ALLOW, IntPtr.Zero);
        ChangeWindowMessageFilterEx(hwnd, WM_COPYGLOBALDATA, MSGFLT_ALLOW, IntPtr.Zero);
    }

    // ── User32 (shell hook) ───────────────────────────────────────────────────

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterShellHookWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool DeregisterShellHookWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern uint RegisterWindowMessage(string lpString);

    /// <summary>Shell hook code posted when a WM_APPCOMMAND reaches the shell.</summary>
    public const int HSHELL_APPCOMMAND = 12;

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

    // ── File security (backup-privilege bypass) ───────────────────────────────

    public static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);
    public const uint FILE_SHARE_READ = 0x00000001;
    public const uint FILE_SHARE_WRITE = 0x00000002;
    public const uint OPEN_EXISTING = 3;
    public const uint GENERIC_READ = 0x80000000;

    /// <summary>
    /// Specific read access mask for files/directories: read data, read attributes, read extended attributes, synchronize.
    /// Use instead of GENERIC_READ for AuthzAccessCheck to avoid over-broad access check masks.
    /// </summary>
    public const uint FILE_GENERIC_READ = 0x120089;

    public const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
    public const uint FILE_FLAG_OPEN_REPARSE_POINT = 0x00200000;
    public const uint FILE_ATTRIBUTE_DIRECTORY = 0x00000010;
    public const uint FILE_ATTRIBUTE_REPARSE_POINT = 0x00000400;
    public const uint INVALID_FILE_ATTRIBUTES = 0xFFFFFFFF;
    public const uint FSCTL_GET_REPARSE_POINT = 0x000900A8;
    public const uint MAXIMUM_REPARSE_DATA_BUFFER_SIZE = 16384;
    public const uint IO_REPARSE_TAG_SYMLINK = 0xA000000C;
    public const uint IO_REPARSE_TAG_MOUNT_POINT = 0xA0000003;
    public const uint READ_CONTROL = 0x00020000;
    public const uint WRITE_DAC = 0x00040000;

    public enum SE_OBJECT_TYPE
    {
        SE_FILE_OBJECT = 1
    }

    [Flags]
    public enum SECURITY_INFORMATION : uint
    {
        OWNER_SECURITY_INFORMATION = 0x00000001,
        DACL_SECURITY_INFORMATION = 0x00000004,
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr CreateFile(
        string lpFileName, uint dwDesiredAccess, uint dwShareMode,
        IntPtr lpSecurityAttributes, uint dwCreationDisposition,
        uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern int GetSecurityInfo(
        IntPtr handle, SE_OBJECT_TYPE ObjectType,
        SECURITY_INFORMATION SecurityInfo,
        out IntPtr pSidOwner, out IntPtr pSidGroup,
        out IntPtr pDacl, out IntPtr pSacl,
        out IntPtr ppSecurityDescriptor);

    [DllImport("advapi32.dll")]
    public static extern uint GetSecurityDescriptorLength(IntPtr pSecurityDescriptor);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool GetSecurityDescriptorDacl(
        IntPtr pSecurityDescriptor,
        [MarshalAs(UnmanagedType.Bool)] out bool lpbDaclPresent,
        out IntPtr pDacl,
        [MarshalAs(UnmanagedType.Bool)] out bool lpbDaclDefaulted);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern int SetSecurityInfo(
        IntPtr handle, SE_OBJECT_TYPE ObjectType,
        SECURITY_INFORMATION SecurityInfo,
        IntPtr psidOwner, IntPtr psidGroup,
        IntPtr pDacl, IntPtr pSacl);

    // ── Shared token helpers ───────────────────────────────────────────────────

    private const uint TokenQuery = 0x0008;

    /// <summary>Token information class for the user SID (TOKEN_USER).</summary>
    public const int TokenUser = 1;

    /// <summary>Token information class for the AppContainer SID (TOKEN_APPCONTAINER_INFORMATION).</summary>
    public const int TokenAppContainerSid = 31;

    /// <summary>Desired access for process handle to query tokens.</summary>
    public const uint ProcessQueryLimitedInformation = 0x1000;

    /// <summary>
    /// Returns the token information class to use for SID lookup based on the SID prefix.
    /// AppContainer SIDs start with "S-1-15-2-"; all other SIDs use TOKEN_USER.
    /// </summary>
    public static int GetTokenInfoClass(string sid) =>
        sid.StartsWith("S-1-15-2-", StringComparison.OrdinalIgnoreCase)
            ? TokenAppContainerSid
            : TokenUser;

    /// <summary>
    /// Reads the SID from a process token using the specified token information class.
    /// TOKEN_USER and TOKEN_APPCONTAINER_INFORMATION share the same leading layout:
    /// a pointer to the SID as the first field.
    /// Returns null on any error (best-effort, used for matching only).
    /// </summary>
    public static string? GetTokenSid(IntPtr hProcess, int tokenInfoClass)
    {
        if (!OpenProcessToken(hProcess, TokenQuery, out var hToken))
            return null;
        try
        {
            GetTokenInformation(hToken, tokenInfoClass, IntPtr.Zero, 0, out var needed);
            if (needed == 0)
                return null;

            var buffer = Marshal.AllocHGlobal((int)needed);
            try
            {
                if (!GetTokenInformation(hToken, tokenInfoClass, buffer, needed, out _))
                    return null;
                var sidPtr = Marshal.ReadIntPtr(buffer);
                if (sidPtr == IntPtr.Zero)
                    return null;
                return new SecurityIdentifier(sidPtr).Value;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        finally
        {
            CloseHandle(hToken);
        }
    }

    // ── File attributes / reparse point resolution ────────────────────────────

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern uint GetFileAttributes(string lpFileName);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool DeviceIoControl(
        IntPtr hDevice, uint dwIoControlCode,
        IntPtr lpInBuffer, uint nInBufferSize,
        [Out] byte[] lpOutBuffer, uint nOutBufferSize,
        out uint lpBytesReturned, IntPtr lpOverlapped);

    [StructLayout(LayoutKind.Sequential)]
    public struct BY_HANDLE_FILE_INFORMATION
    {
        public uint dwFileAttributes;
        public FILETIME ftCreationTime;
        public FILETIME ftLastAccessTime;
        public FILETIME ftLastWriteTime;
        public uint dwVolumeSerialNumber;
        public uint nFileSizeHigh;
        public uint nFileSizeLow;
        public uint nNumberOfLinks;
        public uint nFileIndexHigh;
        public uint nFileIndexLow;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool GetFileInformationByHandle(IntPtr hFile, out BY_HANDLE_FILE_INFORMATION lpFileInformation);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern uint GetFinalPathNameByHandle(IntPtr hFile, StringBuilder lpszFilePath,
        uint cchFilePath, uint dwFlags);

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

    // ── Authz access check ────────────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    public struct LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct AUTHZ_ACCESS_REQUEST
    {
        public uint DesiredAccess;
        public IntPtr PrincipalSelfSid;
        public IntPtr ObjectTypeList;
        public uint ObjectTypeListLength;
        public IntPtr OptionalArguments;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct AUTHZ_ACCESS_REPLY
    {
        public uint ResultListLength;
        public IntPtr GrantedAccessMask;
        public IntPtr SaclEvaluationResults;
        public IntPtr Error;
    }

    [DllImport("authz.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool AuthzInitializeResourceManager(
        uint flags,
        IntPtr pfnAccessCheck,
        IntPtr pfnComputeDynamicGroups,
        IntPtr pfnFreeDynamicGroups,
        string? szResourceManagerName,
        out IntPtr phAuthzResourceManager);

    [DllImport("authz.dll", SetLastError = true)]
    public static extern bool AuthzInitializeContextFromSid(
        uint flags,
        IntPtr UserSid,
        IntPtr hAuthzResourceManager,
        IntPtr pExpirationTime,
        LUID Identifier,
        IntPtr DynamicGroupArgs,
        out IntPtr phAuthzClientContext);

    [DllImport("authz.dll", SetLastError = true)]
    public static extern bool AuthzAccessCheck(
        uint flags,
        IntPtr hAuthzClientContext,
        ref AUTHZ_ACCESS_REQUEST pRequest,
        IntPtr hAuditEvent,
        IntPtr pSecurityDescriptor,
        IntPtr[] OptionalSecurityDescriptorArray,
        uint OptionalSecurityDescriptorCount,
        ref AUTHZ_ACCESS_REPLY pReply,
        IntPtr phAccessCheckResults);

    [DllImport("authz.dll", SetLastError = true)]
    public static extern bool AuthzFreeContext(IntPtr hAuthzClientContext);

    [DllImport("authz.dll", SetLastError = true)]
    public static extern bool AuthzFreeResourceManager(IntPtr hAuthzResourceManager);
}