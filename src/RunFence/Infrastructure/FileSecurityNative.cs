using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace RunFence.Infrastructure;

/// <summary>
/// P/Invoke declarations for file security, reparse points, and authorization checks.
/// Consumed by ACL services. Process, window, and shell declarations have been split into
/// <see cref="ProcessNative"/>, <see cref="WindowNative"/>, and <see cref="ShellNative"/>.
/// File attribute constants shared with shell icon queries also live here.
/// </summary>
public static class FileSecurityNative
{
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
    public const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;
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
