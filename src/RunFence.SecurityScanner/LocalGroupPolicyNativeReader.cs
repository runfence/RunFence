using System.Runtime.InteropServices;
using System.Security.Principal;

namespace RunFence.SecurityScanner;

public interface ILocalGroupPolicyNativeReader
{
    string? GetLocalDomainSid();
    Dictionary<string, string> ResolveLocalGroupNames(IReadOnlyList<string> sidStrings);
    HashSet<string>? GetLocalGroupMemberSids(string groupName);
}

public class LocalGroupPolicyNativeReader : ILocalGroupPolicyNativeReader
{
    [DllImport("advapi32.dll")]
    private static extern int LsaOpenPolicy(
        nint systemName,
        ref OBJECT_ATTRIBUTES objectAttributes,
        uint desiredAccess,
        out nint policyHandle);

    [DllImport("advapi32.dll")]
    private static extern int LsaLookupSids(
        nint policyHandle,
        int count,
        nint[] sids,
        out nint referencedDomains,
        out nint names);

    [DllImport("advapi32.dll")]
    private static extern int LsaFreeMemory(nint buffer);

    [DllImport("advapi32.dll")]
    private static extern int LsaClose(nint objectHandle);

    [DllImport("netapi32.dll")]
    private static extern int NetApiBufferFree(nint buffer);

    [DllImport("netapi32.dll")]
    private static extern int NetUserModalsGet(string? servername, int level, out nint bufptr);

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int NetLocalGroupGetMembers(
        string? servername,
        string localGroupName,
        int level,
        out nint bufptr,
        int prefmaxlen,
        out int entriesread,
        out int totalentries,
        ref nint resumehandle);

    [StructLayout(LayoutKind.Sequential)]
    private struct OBJECT_ATTRIBUTES
    {
        public int Length;
        public nint RootDirectory;
        public nint ObjectName;
        public uint Attributes;
        public nint SecurityDescriptor;
        public nint SecurityQualityOfService;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct USER_MODALS_INFO_2
    {
        public nint usrmod2_domain_name;
        public nint usrmod2_domain_id;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LSA_UNICODE_STRING
    {
        public ushort Length;
        public ushort MaximumLength;
        public nint Buffer;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LSA_TRANSLATED_NAME
    {
        public int Use;
        public LSA_UNICODE_STRING Name;
        public int DomainIndex;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LOCALGROUP_MEMBERS_INFO_0
    {
        public nint lgrmi0_sid;
    }

    private const uint POLICY_LOOKUP_NAMES = 0x00000800;
    private const int STATUS_SUCCESS = 0;
    private const int STATUS_SOME_NOT_MAPPED = 0x00000107;
    private const int SID_TYPE_ALIAS = 4;

    private string? _localDomainSid;

    public string? GetLocalDomainSid()
    {
        if (_localDomainSid != null)
            return _localDomainSid;

        if (NetUserModalsGet(null, 2, out var ptr) != 0)
            return null;

        try
        {
            var info = Marshal.PtrToStructure<USER_MODALS_INFO_2>(ptr);
            if (info.usrmod2_domain_id == nint.Zero)
                return null;
            _localDomainSid = new SecurityIdentifier(info.usrmod2_domain_id).Value;
            return _localDomainSid;
        }
        catch
        {
            return null;
        }
        finally
        {
            NetApiBufferFree(ptr);
        }
    }

    public Dictionary<string, string> ResolveLocalGroupNames(IReadOnlyList<string> sidStrings)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (sidStrings.Count == 0)
            return result;

        var validEntries = new List<(int OriginalIndex, byte[] Binary)>(sidStrings.Count);
        for (int i = 0; i < sidStrings.Count; i++)
        {
            try
            {
                var si = new SecurityIdentifier(sidStrings[i]);
                var bin = new byte[si.BinaryLength];
                si.GetBinaryForm(bin, 0);
                validEntries.Add((i, bin));
            }
            catch
            {
            }
        }

        if (validEntries.Count == 0)
            return result;

        var handles = new GCHandle[validEntries.Count];
        var sidPtrs = new nint[validEntries.Count];
        try
        {
            for (int i = 0; i < validEntries.Count; i++)
            {
                handles[i] = GCHandle.Alloc(validEntries[i].Binary, GCHandleType.Pinned);
                sidPtrs[i] = handles[i].AddrOfPinnedObject();
            }

            var objAttrs = new OBJECT_ATTRIBUTES { Length = Marshal.SizeOf<OBJECT_ATTRIBUTES>() };
            if (LsaOpenPolicy(nint.Zero, ref objAttrs, POLICY_LOOKUP_NAMES, out nint policy) != STATUS_SUCCESS)
                return result;

            try
            {
                int status = LsaLookupSids(policy, validEntries.Count, sidPtrs, out nint domains, out nint names);
                if (status is STATUS_SUCCESS or STATUS_SOME_NOT_MAPPED)
                {
                    try
                    {
                        int stride = Marshal.SizeOf<LSA_TRANSLATED_NAME>();
                        for (int i = 0; i < validEntries.Count; i++)
                        {
                            var entry = Marshal.PtrToStructure<LSA_TRANSLATED_NAME>(names + i * stride);
                            if (entry is { Use: SID_TYPE_ALIAS, Name.Length: > 0 } && entry.Name.Buffer != nint.Zero)
                            {
                                var name = Marshal.PtrToStringUni(entry.Name.Buffer, entry.Name.Length / 2);
                                if (!string.IsNullOrEmpty(name))
                                    result[sidStrings[validEntries[i].OriginalIndex]] = name;
                            }
                        }
                    }
                    finally
                    {
                        LsaFreeMemory(domains);
                        LsaFreeMemory(names);
                    }
                }
            }
            finally
            {
                LsaClose(policy);
            }
        }
        catch
        {
        }
        finally
        {
            foreach (var h in handles)
                if (h.IsAllocated)
                    h.Free();
        }

        return result;
    }

    public HashSet<string>? GetLocalGroupMemberSids(string groupName)
    {
        nint resumeHandle = nint.Zero;
        int status = NetLocalGroupGetMembers(null, groupName, 0, out nint buf, -1, out int entriesRead, out _, ref resumeHandle);
        if (status != 0)
            return null;

        var members = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            int stride = Marshal.SizeOf<LOCALGROUP_MEMBERS_INFO_0>();
            for (int i = 0; i < entriesRead; i++)
            {
                var info = Marshal.PtrToStructure<LOCALGROUP_MEMBERS_INFO_0>(buf + i * stride);
                if (info.lgrmi0_sid != nint.Zero)
                {
                    try
                    {
                        members.Add(new SecurityIdentifier(info.lgrmi0_sid).Value);
                    }
                    catch
                    {
                    }
                }
            }
        }
        finally
        {
            NetApiBufferFree(buf);
        }

        return members;
    }
}
