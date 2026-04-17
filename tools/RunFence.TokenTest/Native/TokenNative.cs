using System.Runtime.InteropServices;

namespace RunFence.TokenTest.Native;

internal static class TokenNative
{
    public const uint TOKEN_ALL_ACCESS = 0xF01FF;
    public const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
    public const uint TOKEN_IMPERSONATE = 0x0004;
    public const uint TOKEN_QUERY = 0x0008;
    public const uint TOKEN_DUPLICATE = 0x0002;

    public const uint DISABLE_MAX_PRIVILEGE = 0x1;

    public const uint SE_GROUP_MANDATORY = 0x00000001;
    public const uint SE_GROUP_ENABLED = 0x00000004;
    public const uint SE_GROUP_USE_FOR_DENY_ONLY = 0x00000010;
    public const uint SE_GROUP_INTEGRITY = 0x00000020;
    public const uint SE_GROUP_LOGON_ID = 0xC0000000;

    public const int TOKEN_SOURCE_LENGTH = 8;

    public const uint SE_PRIVILEGE_ENABLED = 0x00000002;

    public const uint LOGON_WITH_PROFILE = 0x00000001;

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool DuplicateTokenEx(
        IntPtr hExistingToken,
        uint dwDesiredAccess,
        IntPtr lpTokenAttributes,
        SECURITY_IMPERSONATION_LEVEL ImpersonationLevel,
        TOKEN_TYPE TokenType,
        out IntPtr phNewToken);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool CreateRestrictedToken(
        IntPtr ExistingTokenHandle,
        uint Flags,
        uint DisableSidCount,
        [MarshalAs(UnmanagedType.LPArray)] SID_AND_ATTRIBUTES[]? SidsToDisable,
        uint DeletePrivilegeCount,
        IntPtr PrivilegesToDelete,
        uint RestrictedSidCount,
        [MarshalAs(UnmanagedType.LPArray)] SID_AND_ATTRIBUTES[]? SidsToRestrict,
        out IntPtr NewTokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool GetTokenInformation(
        IntPtr TokenHandle,
        TOKEN_INFORMATION_CLASS TokenInformationClass,
        IntPtr TokenInformation,
        uint TokenInformationLength,
        out uint ReturnLength);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool SetTokenInformation(
        IntPtr TokenHandle,
        TOKEN_INFORMATION_CLASS TokenInformationClass,
        IntPtr TokenInformation,
        uint TokenInformationLength);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool LookupPrivilegeValue(string? lpSystemName, string lpName, out LUID lpLuid);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool AdjustTokenPrivileges(
        IntPtr TokenHandle,
        bool DisableAllPrivileges,
        ref TOKEN_PRIVILEGES NewState,
        uint BufferLength,
        IntPtr PreviousState,
        IntPtr ReturnLength);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool ImpersonateLoggedOnUser(IntPtr hToken);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool RevertToSelf();

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool CreateProcessAsUser(
        IntPtr hToken,
        string? lpApplicationName,
        string lpCommandLine,
        IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        bool bInheritHandles,
        uint dwCreationFlags,
        IntPtr lpEnvironment,
        string? lpCurrentDirectory,
        ref STARTUPINFO lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool CreateProcessWithTokenW(
        IntPtr hToken,
        uint dwLogonFlags,
        string? lpApplicationName,
        string lpCommandLine,
        uint dwCreationFlags,
        IntPtr lpEnvironment,
        string? lpCurrentDirectory,
        ref STARTUPINFO lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("ntdll.dll")]
    public static extern int NtCreateToken(
        out IntPtr TokenHandle,
        uint DesiredAccess,
        ref OBJECT_ATTRIBUTES ObjectAttributes,
        TOKEN_TYPE TokenType,
        ref LUID AuthenticationId,
        ref LARGE_INTEGER ExpirationTime,
        ref TOKEN_USER User,
        IntPtr Groups,
        IntPtr Privileges,
        ref TOKEN_OWNER Owner,
        ref TOKEN_PRIMARY_GROUP PrimaryGroup,
        ref TOKEN_DEFAULT_DACL DefaultDacl,
        ref TOKEN_SOURCE Source);

    [DllImport("ntdll.dll")]
    public static extern int NtSetInformationToken(IntPtr TokenHandle, TOKEN_INFORMATION_CLASS TokenInformationClass,
        IntPtr TokenInformation, uint TokenInformationLength);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool AllocateLocallyUniqueId(out LUID Luid);

    [StructLayout(LayoutKind.Sequential)]
    public struct LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct LARGE_INTEGER
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct LUID_AND_ATTRIBUTES
    {
        public LUID Luid;
        public uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct TOKEN_PRIVILEGES
    {
        public uint PrivilegeCount;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1)]
        public LUID_AND_ATTRIBUTES[] Privileges;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SID_AND_ATTRIBUTES
    {
        public IntPtr Sid;
        public uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct TOKEN_USER
    {
        public SID_AND_ATTRIBUTES User;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct TOKEN_OWNER
    {
        public IntPtr Owner;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct TOKEN_PRIMARY_GROUP
    {
        public IntPtr PrimaryGroup;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct TOKEN_DEFAULT_DACL
    {
        public IntPtr DefaultDacl;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct TOKEN_MANDATORY_LABEL
    {
        public SID_AND_ATTRIBUTES Label;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct TOKEN_STATISTICS
    {
        public LUID TokenId;
        public LUID AuthenticationId;
        public LARGE_INTEGER ExpirationTime;
        public TOKEN_TYPE TokenType;
        public SECURITY_IMPERSONATION_LEVEL ImpersonationLevel;
        public uint DynamicCharged;
        public uint DynamicAvailable;
        public uint GroupCount;
        public uint PrivilegeCount;
        public LUID ModifiedId;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct OBJECT_ATTRIBUTES
    {
        public int Length;
        public IntPtr RootDirectory;
        public IntPtr ObjectName;
        public uint Attributes;
        public IntPtr SecurityDescriptor;
        public IntPtr SecurityQualityOfService;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct TOKEN_SOURCE
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = TOKEN_SOURCE_LENGTH)]
        public byte[] SourceName;
        public LUID SourceIdentifier;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct STARTUPINFO
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX, dwY, dwXSize, dwYSize;
        public int dwXCountChars, dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public uint dwProcessId;
        public uint dwThreadId;
    }

    public enum TOKEN_INFORMATION_CLASS
    {
        TokenUser = 1,
        TokenGroups,
        TokenPrivileges,
        TokenOwner,
        TokenPrimaryGroup,
        TokenDefaultDacl,
        TokenSource,
        TokenType,
        TokenImpersonationLevel,
        TokenStatistics,
        TokenRestrictedSids,
        TokenSessionId,
        TokenGroupsAndPrivileges,
        TokenSessionReference,
        TokenSandBoxInert,
        TokenAuditPolicy,
        TokenOrigin,
        TokenElevationType,
        TokenLinkedToken,
        TokenElevation,
        TokenHasRestrictions,
        TokenAccessInformation,
        TokenVirtualizationAllowed,
        TokenVirtualizationEnabled,
        TokenIntegrityLevel,
        TokenUIAccess,
        TokenMandatoryPolicy,
        TokenLogonSid,
        TokenIsAppContainer,
        TokenCapabilities,
        TokenAppContainerSid,
        TokenAppContainerNumber,
        TokenUserClaimAttributes,
        TokenDeviceClaimAttributes,
        TokenRestrictedUserClaimAttributes,
        TokenRestrictedDeviceClaimAttributes,
        TokenDeviceGroups,
        TokenRestrictedDeviceGroups,
        TokenSecurityAttributes,
        TokenIsRestricted,
        MaxTokenInfoClass
    }

    public enum TOKEN_TYPE
    {
        TokenPrimary = 1,
        TokenImpersonation
    }

    public enum SECURITY_IMPERSONATION_LEVEL
    {
        SecurityAnonymous,
        SecurityIdentification,
        SecurityImpersonation,
        SecurityDelegation
    }

    public enum SECURITY_LOGON_TYPE
    {
        Interactive = 2,
        Network = 3,
        Batch = 4,
        Service = 5
    }
}
