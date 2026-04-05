using System.Runtime.InteropServices;

namespace RunFence.Account;

internal static class LsaRightsNative
{
    public const uint PolicyViewLocalInformation = 0x00000001;
    public const uint PolicyLookupNames = 0x00000800;
    public const uint PolicyCreateAccount = 0x00000010;
    public const uint StatusObjectNameNotFound = 0xC0000034;

    [StructLayout(LayoutKind.Sequential)]
    public struct LSA_UNICODE_STRING
    {
        public ushort Length;
        public ushort MaximumLength;
        public IntPtr Buffer;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct LSA_OBJECT_ATTRIBUTES
    {
        public uint Length;
        public IntPtr RootDirectory;
        public IntPtr ObjectName;
        public uint Attributes;
        public IntPtr SecurityDescriptor;
        public IntPtr SecurityQualityOfService;
    }

    [DllImport("advapi32.dll", PreserveSig = true)]
    public static extern uint LsaOpenPolicy(
        IntPtr SystemName,
        ref LSA_OBJECT_ATTRIBUTES ObjectAttributes,
        uint DesiredAccess,
        out IntPtr PolicyHandle);

    [DllImport("advapi32.dll", PreserveSig = true)]
    public static extern uint LsaAddAccountRights(
        IntPtr PolicyHandle,
        byte[] AccountSid,
        LSA_UNICODE_STRING[] UserRights,
        uint CountOfRights);

    [DllImport("advapi32.dll", PreserveSig = true)]
    public static extern uint LsaRemoveAccountRights(
        IntPtr PolicyHandle,
        byte[] AccountSid,
        [MarshalAs(UnmanagedType.U1)] bool AllRights,
        LSA_UNICODE_STRING[] UserRights,
        uint CountOfRights);

    [DllImport("advapi32.dll", PreserveSig = true)]
    public static extern uint LsaEnumerateAccountRights(
        IntPtr PolicyHandle,
        byte[] AccountSid,
        out IntPtr UserRights,
        out uint CountOfRights);

    [DllImport("advapi32.dll", PreserveSig = true)]
    public static extern uint LsaClose(IntPtr PolicyHandle);

    [DllImport("advapi32.dll", PreserveSig = true)]
    public static extern uint LsaNtStatusToWinError(uint Status);

    [DllImport("advapi32.dll", PreserveSig = true)]
    public static extern uint LsaFreeMemory(IntPtr Buffer);
}