using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;
using RunFence.Core;

namespace RunFence.Account;

public class LsaRightsHelper(NTTranslateApi ntTranslate) : ILsaRightsHelper
{
    private const uint PolicyViewLocalInformation = 0x00000001;
    private const uint PolicyLookupNames = 0x00000800;
    private const uint PolicyCreateAccount = 0x00000010;
    private const uint StatusObjectNameNotFound = 0xC0000034;

    [StructLayout(LayoutKind.Sequential)]
    private struct LSA_UNICODE_STRING
    {
        public ushort Length;
        public ushort MaximumLength;
        public IntPtr Buffer;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LSA_OBJECT_ATTRIBUTES
    {
        public uint Length;
        public IntPtr RootDirectory;
        public IntPtr ObjectName;
        public uint Attributes;
        public IntPtr SecurityDescriptor;
        public IntPtr SecurityQualityOfService;
    }

    [DllImport("advapi32.dll", PreserveSig = true)]
    private static extern uint LsaOpenPolicy(
        IntPtr SystemName,
        ref LSA_OBJECT_ATTRIBUTES ObjectAttributes,
        uint DesiredAccess,
        out IntPtr PolicyHandle);

    [DllImport("advapi32.dll", PreserveSig = true)]
    private static extern uint LsaAddAccountRights(
        IntPtr PolicyHandle,
        byte[] AccountSid,
        LSA_UNICODE_STRING[] UserRights,
        uint CountOfRights);

    [DllImport("advapi32.dll", PreserveSig = true)]
    private static extern uint LsaRemoveAccountRights(
        IntPtr PolicyHandle,
        byte[] AccountSid,
        [MarshalAs(UnmanagedType.U1)] bool AllRights,
        LSA_UNICODE_STRING[] UserRights,
        uint CountOfRights);

    [DllImport("advapi32.dll", PreserveSig = true)]
    private static extern uint LsaEnumerateAccountRights(
        IntPtr PolicyHandle,
        byte[] AccountSid,
        out IntPtr UserRights,
        out uint CountOfRights);

    [DllImport("advapi32.dll", PreserveSig = true)]
    private static extern uint LsaClose(IntPtr PolicyHandle);

    [DllImport("advapi32.dll", PreserveSig = true)]
    private static extern uint LsaNtStatusToWinError(uint Status);

    [DllImport("advapi32.dll", PreserveSig = true)]
    private static extern uint LsaFreeMemory(IntPtr Buffer);

    public const string SeDenyNetworkLogonRight = "SeDenyNetworkLogonRight";
    public const string SeDenyRemoteInteractiveLogonRight = "SeDenyRemoteInteractiveLogonRight";
    public const string SeDenyBatchLogonRight = "SeDenyBatchLogonRight";
    public const string SeDenyServiceLogonRight = "SeDenyServiceLogonRight";
    public const string SeInteractiveLogonRight = "SeInteractiveLogonRight";

    public byte[] GetSidBytes(string sidString)
    {
        var sid = new SecurityIdentifier(sidString);
        return SidToBytes(sid);
    }

    public byte[]? TryResolveSidBytes(string? domain, string username)
    {
        try
        {
            var sid = ntTranslate.TranslateSid(domain, username);
            return SidToBytes(sid);
        }
        catch
        {
            return null;
        }
    }

    private byte[] SidToBytes(SecurityIdentifier sid)
    {
        var sidBytes = new byte[sid.BinaryLength];
        sid.GetBinaryForm(sidBytes, 0);
        return sidBytes;
    }

    public List<string> EnumerateAccountRights(byte[] sidBytes)
    {
        var rights = new List<string>();
        WithLsaPolicy(PolicyLookupNames | PolicyViewLocalInformation, policyHandle =>
        {
            var status = LsaEnumerateAccountRights(policyHandle, sidBytes, out var rightsPtr, out var count);
            if (status == StatusObjectNameNotFound)
                return; // No rights assigned
            if (status != 0)
                ThrowLsaError("LsaEnumerateAccountRights", status);

            try
            {
                var ptr = rightsPtr;
                var structSize = Marshal.SizeOf<LSA_UNICODE_STRING>();
                for (uint i = 0; i < count; i++)
                {
                    var lsaStr = Marshal.PtrToStructure<LSA_UNICODE_STRING>(ptr);
                    rights.Add(Marshal.PtrToStringUni(lsaStr.Buffer, lsaStr.Length / sizeof(char)));
                    ptr += structSize;
                }
            }
            finally
            {
                LsaFreeMemory(rightsPtr);
            }
        });
        return rights;
    }

    public void AddAccountRights(byte[] sidBytes, string[] rights)
    {
        WithLsaPolicy(PolicyLookupNames | PolicyCreateAccount, policyHandle =>
        {
            var lsaRights = CreateLsaStrings(rights);
            try
            {
                var status = LsaAddAccountRights(policyHandle, sidBytes, lsaRights, (uint)lsaRights.Length);
                if (status != 0)
                    ThrowLsaError("LsaAddAccountRights", status);
            }
            finally
            {
                FreeLsaStrings(lsaRights);
            }
        });
    }

    public void RemoveAccountRights(byte[] sidBytes, string[] rights)
    {
        WithLsaPolicy(PolicyLookupNames, policyHandle =>
        {
            var lsaRights = CreateLsaStrings(rights);
            try
            {
                var status = LsaRemoveAccountRights(policyHandle, sidBytes, false, lsaRights, (uint)lsaRights.Length);
                // StatusObjectNameNotFound is OK — rights weren't assigned
                if (status != 0 && status != StatusObjectNameNotFound)
                    ThrowLsaError("LsaRemoveAccountRights", status);
            }
            finally
            {
                FreeLsaStrings(lsaRights);
            }
        });
    }

    private void WithLsaPolicy(uint desiredAccess, Action<IntPtr> action)
    {
        var attrs = new LSA_OBJECT_ATTRIBUTES();
        var status = LsaOpenPolicy(IntPtr.Zero, ref attrs, desiredAccess, out var policyHandle);
        if (status != 0)
            ThrowLsaError("LsaOpenPolicy", status);

        try
        {
            action(policyHandle);
        }
        finally
        {
            LsaClose(policyHandle);
        }
    }

    private void ThrowLsaError(string functionName, uint status)
    {
        throw new InvalidOperationException(
            $"{functionName} failed: {new Win32Exception((int)LsaNtStatusToWinError(status)).Message}");
    }

    private LSA_UNICODE_STRING[] CreateLsaStrings(string[] rights)
    {
        var result = new LSA_UNICODE_STRING[rights.Length];
        for (int i = 0; i < rights.Length; i++)
        {
            result[i] = new LSA_UNICODE_STRING
            {
                Length = (ushort)(rights[i].Length * sizeof(char)),
                MaximumLength = (ushort)((rights[i].Length + 1) * sizeof(char)),
                Buffer = Marshal.StringToHGlobalUni(rights[i])
            };
        }

        return result;
    }

    private void FreeLsaStrings(LSA_UNICODE_STRING[] strings)
    {
        foreach (var s in strings)
        {
            if (s.Buffer != IntPtr.Zero)
                Marshal.FreeHGlobal(s.Buffer);
        }
    }
}