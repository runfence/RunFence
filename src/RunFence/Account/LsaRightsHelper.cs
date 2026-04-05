using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;
using RunFence.Core;

namespace RunFence.Account;

public class LsaRightsHelper(NTTranslateApi ntTranslate) : ILsaRightsHelper
{
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
        WithLsaPolicy(LsaRightsNative.PolicyLookupNames | LsaRightsNative.PolicyViewLocalInformation, policyHandle =>
        {
            var status = LsaRightsNative.LsaEnumerateAccountRights(policyHandle, sidBytes, out var rightsPtr, out var count);
            if (status == LsaRightsNative.StatusObjectNameNotFound)
                return; // No rights assigned
            if (status != 0)
                ThrowLsaError("LsaEnumerateAccountRights", status);

            try
            {
                var ptr = rightsPtr;
                var structSize = Marshal.SizeOf<LsaRightsNative.LSA_UNICODE_STRING>();
                for (uint i = 0; i < count; i++)
                {
                    var lsaStr = Marshal.PtrToStructure<LsaRightsNative.LSA_UNICODE_STRING>(ptr);
                    rights.Add(Marshal.PtrToStringUni(lsaStr.Buffer, lsaStr.Length / sizeof(char)));
                    ptr += structSize;
                }
            }
            finally
            {
                LsaRightsNative.LsaFreeMemory(rightsPtr);
            }
        });
        return rights;
    }

    public void AddAccountRights(byte[] sidBytes, string[] rights)
    {
        WithLsaPolicy(LsaRightsNative.PolicyLookupNames | LsaRightsNative.PolicyCreateAccount, policyHandle =>
        {
            var lsaRights = CreateLsaStrings(rights);
            try
            {
                var status = LsaRightsNative.LsaAddAccountRights(policyHandle, sidBytes, lsaRights, (uint)lsaRights.Length);
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
        WithLsaPolicy(LsaRightsNative.PolicyLookupNames, policyHandle =>
        {
            var lsaRights = CreateLsaStrings(rights);
            try
            {
                var status = LsaRightsNative.LsaRemoveAccountRights(policyHandle, sidBytes, false, lsaRights, (uint)lsaRights.Length);
                // StatusObjectNameNotFound is OK — rights weren't assigned
                if (status != 0 && status != LsaRightsNative.StatusObjectNameNotFound)
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
        var attrs = new LsaRightsNative.LSA_OBJECT_ATTRIBUTES();
        var status = LsaRightsNative.LsaOpenPolicy(IntPtr.Zero, ref attrs, desiredAccess, out var policyHandle);
        if (status != 0)
            ThrowLsaError("LsaOpenPolicy", status);

        try
        {
            action(policyHandle);
        }
        finally
        {
            LsaRightsNative.LsaClose(policyHandle);
        }
    }

    private void ThrowLsaError(string functionName, uint status)
    {
        throw new InvalidOperationException(
            $"{functionName} failed: {new Win32Exception((int)LsaRightsNative.LsaNtStatusToWinError(status)).Message}");
    }

    private LsaRightsNative.LSA_UNICODE_STRING[] CreateLsaStrings(string[] rights)
    {
        var result = new LsaRightsNative.LSA_UNICODE_STRING[rights.Length];
        for (int i = 0; i < rights.Length; i++)
        {
            result[i] = new LsaRightsNative.LSA_UNICODE_STRING
            {
                Length = (ushort)(rights[i].Length * sizeof(char)),
                MaximumLength = (ushort)((rights[i].Length + 1) * sizeof(char)),
                Buffer = Marshal.StringToHGlobalUni(rights[i])
            };
        }

        return result;
    }

    private void FreeLsaStrings(LsaRightsNative.LSA_UNICODE_STRING[] strings)
    {
        foreach (var s in strings)
        {
            if (s.Buffer != IntPtr.Zero)
                Marshal.FreeHGlobal(s.Buffer);
        }
    }
}