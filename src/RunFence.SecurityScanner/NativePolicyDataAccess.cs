using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32;

namespace RunFence.SecurityScanner;

/// <summary>
/// Implements policy and service-state queries that require P/Invoke:
/// account lockout policy (NetAPI/SAM), Windows Firewall service state (SCM),
/// drive root enumeration (QueryDosDevice for SUBST detection),
/// and local group membership resolution (LsaLookupSids/NetLocalGroupGetMembers).
/// Extracted from <see cref="DefaultScannerDataAccess"/> to isolate P/Invoke declarations.
///
/// Note: The native P/Invoke declarations and their direct wrapper methods (SAM, SCM, LSA,
/// NetLocalGroup, Drive APIs) are tightly coupled native interop — not business logic to extract.
/// </summary>
public class NativePolicyDataAccess
{
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern uint QueryDosDevice(string lpDeviceName, char[]? lpTargetPath, uint ucchMax);

    [DllImport("netapi32.dll")]
    private static extern int NetUserModalsGet(string? servername, int level, out nint bufptr);

    [DllImport("netapi32.dll")]
    private static extern int NetApiBufferFree(nint buffer);

    [DllImport("samlib.dll")]
    private static extern int SamConnect(nint serverName, out nint serverHandle, uint desiredAccess,
        ref OBJECT_ATTRIBUTES objectAttributes);

    [DllImport("samlib.dll")]
    private static extern int SamOpenDomain(nint serverHandle, uint desiredAccess, nint domainId,
        out nint domainHandle);

    [DllImport("samlib.dll")]
    private static extern int SamQueryInformationDomain(nint domainHandle, int informationClass,
        out nint buffer);

    [DllImport("samlib.dll")]
    private static extern int SamCloseHandle(nint handle);

    [DllImport("samlib.dll")]
    private static extern int SamFreeMemory(nint buffer);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern nint OpenSCManager(string? machineName, string? databaseName, uint dwAccess);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern nint OpenService(nint hSCManager, string lpServiceName, uint dwDesiredAccess);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool QueryServiceStatus(nint hService, ref SERVICE_STATUS lpServiceStatus);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool CloseServiceHandle(nint hSCObject);

    [StructLayout(LayoutKind.Sequential)]
    private struct SERVICE_STATUS
    {
        public uint dwServiceType;
        public uint dwCurrentState;
        public uint dwControlsAccepted;
        public uint dwWin32ExitCode;
        public uint dwServiceSpecificExitCode;
        public uint dwCheckPoint;
        public uint dwWaitHint;
    }

    private const uint SERVICE_RUNNING = 4;
    private const uint SC_MANAGER_CONNECT = 0x0001;
    private const uint SERVICE_QUERY_STATUS = 0x0004;

    [StructLayout(LayoutKind.Sequential)]
    private struct USER_MODALS_INFO_2
    {
        public nint usrmod2_domain_name;
        public nint usrmod2_domain_id;
    }

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
    private struct DOMAIN_PASSWORD_INFORMATION
    {
        public ushort MinPasswordLength;
        public ushort PasswordHistoryLength;
        public uint PasswordProperties;
        public long MaxPasswordAge;
        public long MinPasswordAge;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct USER_MODALS_INFO_3
    {
        public uint usrmod3_lockout_duration;
        public uint usrmod3_lockout_observation_window;
        public uint usrmod3_lockout_threshold;
    }

    public int? GetAccountLockoutThreshold()
    {
        try
        {
            int result = NetUserModalsGet(null, 3, out var bufPtr);
            if (result != 0)
                return null;
            try
            {
                var info = Marshal.PtrToStructure<USER_MODALS_INFO_3>(bufPtr);
                return (int)info.usrmod3_lockout_threshold;
            }
            finally
            {
                NetApiBufferFree(bufPtr);
            }
        }
        catch
        {
            return null;
        }
    }

    public bool? GetAdminAccountLockoutEnabled()
    {
        // DOMAIN_LOCKOUT_ADMINS flag in DOMAIN_PASSWORD_INFORMATION.PasswordProperties:
        // Set (1) = admin lockout enabled; Clear (0) = admin lockout disabled
        const uint domainLockoutAdmins = 0x00000008u;
        const uint samServerConnect = 0x00000001u;
        const uint samServerLookupDomain = 0x00000020u;
        const uint domainReadPasswordParameters = 0x00000001u;
        const int domainPasswordInformation = 1;
        try
        {
            int result = NetUserModalsGet(null, 2, out var modalsPtr);
            if (result != 0)
                return null;
            try
            {
                var modals2 = Marshal.PtrToStructure<USER_MODALS_INFO_2>(modalsPtr);
                var domainSid = modals2.usrmod2_domain_id;
                if (domainSid == nint.Zero)
                    return null;

                var objAttrs = new OBJECT_ATTRIBUTES { Length = Marshal.SizeOf<OBJECT_ATTRIBUTES>() };
                if (SamConnect(nint.Zero, out var serverHandle, samServerConnect | samServerLookupDomain, ref objAttrs) != 0)
                    return null;
                try
                {
                    if (SamOpenDomain(serverHandle, domainReadPasswordParameters, domainSid, out var domainHandle) != 0)
                        return null;
                    try
                    {
                        if (SamQueryInformationDomain(domainHandle, domainPasswordInformation, out var infoPtr) != 0)
                            return null;
                        try
                        {
                            var info = Marshal.PtrToStructure<DOMAIN_PASSWORD_INFORMATION>(infoPtr);
                            return (info.PasswordProperties & domainLockoutAdmins) != 0;
                        }
                        finally
                        {
                            SamFreeMemory(infoPtr);
                        }
                    }
                    finally
                    {
                        SamCloseHandle(domainHandle);
                    }
                }
                finally
                {
                    SamCloseHandle(serverHandle);
                }
            }
            finally
            {
                NetApiBufferFree(modalsPtr);
            }
        }
        catch
        {
            return null;
        }
    }

    public (bool IsDisabled, bool IsStopped)? GetWindowsFirewallServiceState()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\MpsSvc");
            if (key == null)
                return null;
            bool isDisabled = key.GetValue("Start") is int and 4;

            bool isStopped = false;
            var scm = OpenSCManager(null, null, SC_MANAGER_CONNECT);
            if (scm != nint.Zero)
            {
                try
                {
                    var svc = OpenService(scm, "MpsSvc", SERVICE_QUERY_STATUS);
                    if (svc != nint.Zero)
                    {
                        try
                        {
                            var status = new SERVICE_STATUS();
                            if (QueryServiceStatus(svc, ref status))
                                isStopped = status.dwCurrentState != SERVICE_RUNNING;
                        }
                        finally
                        {
                            CloseServiceHandle(svc);
                        }
                    }
                }
                finally
                {
                    CloseServiceHandle(scm);
                }
            }

            return (isDisabled, isStopped);
        }
        catch
        {
            return null;
        }
    }

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
    private const int SID_TYPE_ALIAS = 4; // SidTypeAlias: local SAM group

    private string? _localDomainSid;

    /// <summary>
    /// Returns the local machine's domain SID prefix (e.g. "S-1-5-21-X-Y-Z"), used to
    /// distinguish local SIDs from domain SIDs without a network roundtrip.
    /// </summary>
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

    /// <summary>
    /// Resolves all provided SID strings in a single <c>LsaLookupSids</c> call and returns
    /// those that are local SAM groups (SidTypeAlias), mapping SID string → group name.
    /// SIDs that are users, well-known groups, or cannot be resolved are omitted.
    /// </summary>
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
            catch { /* invalid SID string — skip */ }
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
                int status = LsaLookupSids(policy, validEntries.Count, sidPtrs,
                    out nint domains, out nint names);
                if (status == STATUS_SUCCESS || status == STATUS_SOME_NOT_MAPPED)
                {
                    try
                    {
                        int stride = Marshal.SizeOf<LSA_TRANSLATED_NAME>();
                        for (int i = 0; i < validEntries.Count; i++)
                        {
                            var entry = Marshal.PtrToStructure<LSA_TRANSLATED_NAME>(names + i * stride);
                            if (entry.Use == SID_TYPE_ALIAS
                                && entry.Name.Length > 0
                                && entry.Name.Buffer != nint.Zero)
                            {
                                var name = Marshal.PtrToStringUni(entry.Name.Buffer,
                                    entry.Name.Length / 2);
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
        catch { /* return partially-filled result */ }
        finally
        {
            foreach (var h in handles)
                if (h.IsAllocated) h.Free();
        }

        return result;
    }

    /// <summary>
    /// Enumerates direct member SIDs of a local SAM group by name via <c>NetLocalGroupGetMembers</c>.
    /// Returns null if the group cannot be enumerated.
    /// </summary>
    public HashSet<string>? GetLocalGroupMemberSids(string groupName)
    {
        nint resumeHandle = nint.Zero;
        int status = NetLocalGroupGetMembers(null, groupName, 0, out nint buf, -1,
            out int entriesRead, out _, ref resumeHandle);
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
                    try { members.Add(new SecurityIdentifier(info.lgrmi0_sid).Value); }
                    catch { }
                }
            }
        }
        finally
        {
            NetApiBufferFree(buf);
        }

        return members;
    }

    public IEnumerable<string> GetDriveRoots()
    {
        var systemDrive = Path.GetPathRoot(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows));

        foreach (var drive in DriveInfo.GetDrives())
        {
            if (!drive.IsReady)
                continue;
            if (drive.DriveType == DriveType.Network)
                continue;

            var root = drive.RootDirectory.FullName;
            if (string.Equals(root, systemDrive, StringComparison.OrdinalIgnoreCase))
                continue;

            // Exclude SUBST drives: their device path starts with \??\
            var driveName = root.TrimEnd('\\');
            var buf = new char[256];
            var len = QueryDosDevice(driveName, buf, (uint)buf.Length);
            if (len > 0)
            {
                var firstNull = Array.IndexOf(buf, '\0', 0, (int)len);
                var devicePath = firstNull >= 0 ? new string(buf, 0, firstNull) : new string(buf, 0, (int)len);
                if (devicePath.StartsWith(@"\??\", StringComparison.Ordinal))
                    continue;
            }
            // If QueryDosDevice fails, include the drive (fail open)

            yield return root;
        }
    }
}