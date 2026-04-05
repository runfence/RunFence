using System.Runtime.InteropServices;
using System.Text;

namespace RunFence.Core;

internal static class SidResolverNative
{
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool LookupAccountNameW(
        string? systemName,
        string accountName,
        IntPtr sid,
        ref uint cbSid,
        StringBuilder? referencedDomainName,
        ref uint cchReferencedDomainName,
        out uint peUse);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool ConvertSidToStringSidW(IntPtr pSid, out IntPtr stringSid);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool ConvertStringSidToSidW(string stringSid, out IntPtr sid);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool LookupAccountSidW(
        string? systemName,
        IntPtr sid,
        StringBuilder? name,
        ref uint cbName,
        StringBuilder? referencedDomainName,
        ref uint cbReferencedDomainName,
        out uint peUse);

    [DllImport("kernel32.dll")]
    public static extern IntPtr LocalFree(IntPtr hMem);

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

    [StructLayout(LayoutKind.Sequential)]
    public struct POLICY_ACCOUNT_DOMAIN_INFO
    {
        public LSA_UNICODE_STRING DomainName;
        public IntPtr DomainSid;
    }

    public const uint PolicyViewLocalInformation = 0x00000001;
    public const int PolicyAccountDomainInformation = 5;

    [DllImport("advapi32.dll", PreserveSig = true)]
    public static extern uint LsaOpenPolicy(
        IntPtr systemName,
        ref LSA_OBJECT_ATTRIBUTES objectAttributes,
        uint desiredAccess,
        out IntPtr handle);

    [DllImport("advapi32.dll", PreserveSig = true)]
    public static extern uint LsaQueryInformationPolicy(
        IntPtr policyHandle,
        int infoClass,
        out IntPtr buffer);

    [DllImport("advapi32.dll", PreserveSig = true)]
    public static extern uint LsaFreeMemory(IntPtr buffer);

    [DllImport("advapi32.dll", PreserveSig = true)]
    public static extern uint LsaClose(IntPtr handle);
}
