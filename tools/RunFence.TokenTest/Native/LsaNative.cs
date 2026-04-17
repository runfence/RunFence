using System.Runtime.InteropServices;

namespace RunFence.TokenTest.Native;

internal static class LsaNative
{
    [DllImport("secur32.dll", SetLastError = true)]
    public static extern uint LsaConnectUntrusted(out IntPtr LsaHandle);

    // Establishes a trusted connection to the LSA. Requires SeTcbPrivilege in the calling thread token.
    // Unlike LsaConnectUntrusted, the resulting handle supports S4U logons via LsaLogonUser.
    [DllImport("secur32.dll", SetLastError = true)]
    public static extern uint LsaRegisterLogonProcess(
        ref LSA_STRING LogonProcessName,
        out IntPtr LsaHandle,
        out ulong SecurityMode);

    [DllImport("secur32.dll", SetLastError = true)]
    public static extern uint LsaLookupAuthenticationPackage(
        IntPtr LsaHandle,
        ref LSA_STRING PackageName,
        out uint AuthenticationPackage);

    [DllImport("secur32.dll", SetLastError = true)]
    public static extern uint LsaLogonUser(
        IntPtr LsaHandle,
        ref LSA_STRING OriginName,
        uint LogonType,
        uint AuthenticationPackage,
        IntPtr AuthenticationInformation,
        uint AuthenticationInformationLength,
        IntPtr LocalGroups,
        ref TOKEN_SOURCE SourceContext,
        out IntPtr ProfileBuffer,
        out uint ProfileBufferLength,
        out TokenNative.LUID LogonId,
        out IntPtr Token,
        out QUOTA_LIMITS Quotas,
        out uint SubStatus);

    [DllImport("secur32.dll", SetLastError = true)]
    public static extern uint LsaFreeReturnBuffer(IntPtr Buffer);

    [DllImport("secur32.dll", SetLastError = true)]
    public static extern uint LsaDeregisterLogonProcess(IntPtr LsaHandle);

    [DllImport("advapi32.dll")]
    public static extern uint LsaNtStatusToWinError(uint Status);

    [StructLayout(LayoutKind.Sequential)]
    public struct LSA_STRING
    {
        public ushort Length;
        public ushort MaximumLength;
        public IntPtr Buffer; // pointer to ANSI char array
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct LSA_UNICODE_STRING
    {
        public ushort Length;       // byte count (not including null)
        public ushort MaximumLength;
        public IntPtr Buffer;       // pointer to UTF-16 char array
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSV1_0_S4U_LOGON
    {
        public int MessageType;  // = 12 (MsV1_0S4ULogon)
        public uint Flags;
        public LSA_UNICODE_STRING UserPrincipalName;
        public LSA_UNICODE_STRING DomainName;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct TOKEN_SOURCE
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = TokenNative.TOKEN_SOURCE_LENGTH)]
        public byte[] SourceName;
        public TokenNative.LUID SourceIdentifier;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct QUOTA_LIMITS
    {
        public IntPtr PagedPoolLimit;
        public IntPtr NonPagedPoolLimit;
        public IntPtr MinimumWorkingSetSize;
        public IntPtr MaximumWorkingSetSize;
        public IntPtr PagefileLimit;
        public long TimeLimit;
    }
}
