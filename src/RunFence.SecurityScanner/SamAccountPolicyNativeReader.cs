using System.Runtime.InteropServices;

namespace RunFence.SecurityScanner;

public interface ISamAccountPolicyNativeReader
{
    int? GetAccountLockoutThreshold();
    bool? GetAdminAccountLockoutEnabled();
}

public class SamAccountPolicyNativeReader : ISamAccountPolicyNativeReader
{
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
    private struct USER_MODALS_INFO_2
    {
        public nint usrmod2_domain_name;
        public nint usrmod2_domain_id;
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
}
