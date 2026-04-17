using System.Runtime.InteropServices;
using System.Text;
using RunFence.TokenTest.Native;

namespace RunFence.TokenTest;

internal class NtCreateTokenHelper
{
    private readonly SystemTokenHelper _systemTokenHelper;
    private readonly TokenHelper _tokenHelper;

    public NtCreateTokenHelper(SystemTokenHelper systemTokenHelper, TokenHelper tokenHelper)
    {
        _systemTokenHelper = systemTokenHelper;
        _tokenHelper = tokenHelper;
    }

    public IntPtr GetCustomToken(IntPtr hCurrentToken, bool omitAdmins = false, bool standardUserPrivileges = false, bool twoPrivileges = false, TokenNative.LUID? authIdOverride = null)
    {
        _systemTokenHelper.EnableDebugPrivilege();

        IntPtr hLsass = _systemTokenHelper.GetLsassToken();
        if (hLsass == IntPtr.Zero)
            throw new InvalidOperationException($"Could not get lsass token: {TokenHelper.GetLastError()}");

        if (!_systemTokenHelper.ImpersonateToken(hLsass))
        {
            ProcessNative.CloseHandle(hLsass);
            throw new InvalidOperationException($"ImpersonateToken(lsass) failed: {TokenHelper.GetLastError()}");
        }
        ProcessNative.CloseHandle(hLsass);

        try
        {
            var stats = _tokenHelper.GetTokenStatistics(hCurrentToken);
            var authId = authIdOverride ?? stats.AuthenticationId;

            // TokenUser — sidPtr points into userRawBuf
            var (userSid, userRawBuf) = GetTokenUserRaw(hCurrentToken);
            var tokenUser = new TokenNative.TOKEN_USER
            {
                User = new TokenNative.SID_AND_ATTRIBUTES { Sid = userSid, Attributes = 0 }
            };

            // TokenGroups — keep all groups; set Admins to DENY_ONLY; preserve logon SID flags
            IntPtr adminsSid = _tokenHelper.GetAdminsSid();
            var (rawGroupsBuf, rawGroups) = _tokenHelper.GetTokenGroups(hCurrentToken);

            // Allocate Medium IL SID — will be added to groups in place of the existing IL SID.
            // Including IL directly in TOKEN_GROUPS avoids a separate SetTokenInformation call that
            // requires SeRelabelPrivilege (which may not be available).
            IntPtr mediumILSid = _tokenHelper.GetMediumIntegritySid();
            bool mediumILPlaced = false;

            var modifiedGroups = new List<(IntPtr sid, uint attrs)>(rawGroups.Count + 1);
            foreach (var (sid, attrs) in rawGroups)
            {
                if ((attrs & TokenNative.SE_GROUP_INTEGRITY) != 0)
                {
                    // Replace the source token's IL SID with Medium IL SID inline.
                    modifiedGroups.Add((mediumILSid, TokenNative.SE_GROUP_INTEGRITY | TokenNative.SE_GROUP_MANDATORY | TokenNative.SE_GROUP_ENABLED));
                    mediumILPlaced = true;
                    continue;
                }

                if (IsSameSid(sid, adminsSid))
                {
                    if (omitAdmins) continue; // completely exclude Admins SID
                    // SE_GROUP_USE_FOR_DENY_ONLY only — adding SE_GROUP_MANDATORY causes STATUS_INVALID_PARAMETER in NtCreateToken
                    modifiedGroups.Add((TokenHelper.CopySid(sid), TokenNative.SE_GROUP_USE_FOR_DENY_ONLY));
                    continue;
                }

                IntPtr sidCopy = TokenHelper.CopySid(sid);
                uint newAttrs = attrs;
                if ((attrs & TokenNative.SE_GROUP_LOGON_ID) == TokenNative.SE_GROUP_LOGON_ID)
                    newAttrs = TokenNative.SE_GROUP_LOGON_ID | TokenNative.SE_GROUP_MANDATORY | TokenNative.SE_GROUP_ENABLED;
                modifiedGroups.Add((sidCopy, newAttrs));
            }
            // If the source token had no IL SID in groups (unusual), append it now.
            if (!mediumILPlaced)
                modifiedGroups.Add((mediumILSid, TokenNative.SE_GROUP_INTEGRITY | TokenNative.SE_GROUP_MANDATORY | TokenNative.SE_GROUP_ENABLED));

            Marshal.FreeHGlobal(adminsSid);
            Marshal.FreeHGlobal(rawGroupsBuf); // rawGroups SID ptrs pointed into rawGroupsBuf; done with them now

            IntPtr groupsBuf = BuildTokenGroupsBuffer(modifiedGroups);

            Console.WriteLine($"  [diag] NtCreateToken: {modifiedGroups.Count} groups (omitAdmins={omitAdmins}, stdPrivs={standardUserPrivileges}):");
            foreach (var (sid, attrs) in modifiedGroups)
            {
                SecurityNative.ConvertSidToStringSid(sid, out var sidStr);
                Console.WriteLine($"    SID={sidStr ?? "?"} Attrs=0x{attrs:X8}");
            }
            SecurityNative.ConvertSidToStringSid(userSid, out var ownerSidStr);

            // TokenPrivileges
            IntPtr privsBuf = standardUserPrivileges
                ? BuildStandardUserPrivilegesBuffer()
                : twoPrivileges
                    ? BuildTwoPrivilegesBuffer()
                    : BuildMinimalPrivilegesBuffer();

            // Owner must be an enabled SID; use user SID (original owner may be Administrators = DENY_ONLY)
            var tokenOwner = new TokenNative.TOKEN_OWNER { Owner = userSid };

            // PrimaryGroup must be a SID present in TOKEN_GROUPS — read from current token (user SID is NOT in TOKEN_GROUPS)
            var (actualPrimaryGroupSid, primaryGroupRawBuf) = _tokenHelper.GetTokenPrimaryGroup(hCurrentToken);
            var tokenPrimaryGroup = new TokenNative.TOKEN_PRIMARY_GROUP { PrimaryGroup = actualPrimaryGroupSid };

            SecurityNative.ConvertSidToStringSid(actualPrimaryGroupSid, out var primaryGroupSidStr);
            Console.WriteLine($"  [diag] Owner={ownerSidStr ?? "?"} PrimaryGroup={primaryGroupSidStr ?? "?"}");

            // TokenDefaultDacl — dacl ptr points into daclRawBuf
            var (dacl, daclRawBuf) = _tokenHelper.GetTokenDefaultDacl(hCurrentToken);
            var tokenDefaultDacl = new TokenNative.TOKEN_DEFAULT_DACL { DefaultDacl = dacl };

            var objAttr = new TokenNative.OBJECT_ATTRIBUTES
            {
                Length = Marshal.SizeOf<TokenNative.OBJECT_ATTRIBUTES>()
            };
            var expiry = new TokenNative.LARGE_INTEGER { LowPart = 0xFFFFFFFF, HighPart = 0x7FFFFFFF };

            if (!TokenNative.AllocateLocallyUniqueId(out var srcLuid))
                throw new InvalidOperationException($"AllocateLocallyUniqueId failed: {TokenHelper.GetLastError()}");

            var tokenSource = new TokenNative.TOKEN_SOURCE
            {
                SourceName = Encoding.ASCII.GetBytes("TokenTst"),
                SourceIdentifier = srcLuid
            };

            int ntStatus = TokenNative.NtCreateToken(
                out IntPtr hNewToken,
                TokenNative.TOKEN_ALL_ACCESS,
                ref objAttr,
                TokenNative.TOKEN_TYPE.TokenPrimary,
                ref authId,
                ref expiry,
                ref tokenUser,
                groupsBuf,
                privsBuf,
                ref tokenOwner,
                ref tokenPrimaryGroup,
                ref tokenDefaultDacl,
                ref tokenSource);

            FreeGroupsBuffer(groupsBuf, modifiedGroups);
            Marshal.FreeHGlobal(privsBuf);
            Marshal.FreeHGlobal(userRawBuf);
            Marshal.FreeHGlobal(daclRawBuf);
            Marshal.FreeHGlobal(primaryGroupRawBuf);

            if (ntStatus != 0)
            {
                uint winErr = LsaNative.LsaNtStatusToWinError((uint)ntStatus);
                throw new InvalidOperationException(
                    $"NtCreateToken failed: NTSTATUS=0x{ntStatus:X8} ({TokenHelper.FormatError(winErr)})");
            }

            // Medium IL was included directly in TOKEN_GROUPS — no separate SetTokenInformation needed.
            return hNewToken;
        }
        finally
        {
            _systemTokenHelper.RevertImpersonation();
        }
    }

    private static bool IsSameSid(IntPtr a, IntPtr b)
    {
        SecurityNative.ConvertSidToStringSid(a, out var s1);
        SecurityNative.ConvertSidToStringSid(b, out var s2);
        return s1 != null && s1 == s2;
    }

    // TOKEN_GROUPS layout: DWORD GroupCount [+ 4 pad on 64-bit] + N × SID_AND_ATTRIBUTES
    // SID_AND_ATTRIBUTES: PSID(8) + DWORD(4) + [4 pad] = 16 bytes on 64-bit; PSID(4) + DWORD(4) = 8 bytes on 32-bit
    private static IntPtr BuildTokenGroupsBuffer(List<(IntPtr sid, uint attrs)> groups)
    {
        int elemSize = IntPtr.Size == 8 ? 16 : 8;
        int headerSize = IntPtr.Size == 8 ? 8 : 4; // DWORD + optional padding
        int size = headerSize + groups.Count * elemSize;
        IntPtr buf = Marshal.AllocHGlobal(size);
        Marshal.WriteInt32(buf, groups.Count);
        int offset = headerSize;
        foreach (var (sid, attrs) in groups)
        {
            Marshal.WriteIntPtr(buf, offset, sid);
            Marshal.WriteInt32(buf, offset + IntPtr.Size, (int)attrs);
            offset += elemSize;
        }
        return buf;
    }

    private static void FreeGroupsBuffer(IntPtr buf, List<(IntPtr sid, uint attrs)> groups)
    {
        foreach (var (sid, _) in groups)
            Marshal.FreeHGlobal(sid);
        Marshal.FreeHGlobal(buf);
    }

    private static IntPtr BuildMinimalPrivilegesBuffer()
    {
        TokenNative.LookupPrivilegeValue(null, "SeChangeNotifyPrivilege", out var luid);
        return BuildTokenPrivilegesBuffer(new[] { (luid, TokenNative.SE_PRIVILEGE_ENABLED) });
    }

    // Builds a two-privilege set: SeChangeNotifyPrivilege (enabled) + SeIncreaseWorkingSetPrivilege (disabled).
    // Used to isolate whether any second privilege is sufficient, or a specific one is required.
    private static IntPtr BuildTwoPrivilegesBuffer()
    {
        var privs = new List<(TokenNative.LUID, uint)>();
        foreach (var (name, enabled) in new (string, bool)[]
        {
            ("SeChangeNotifyPrivilege",       true),
            ("SeIncreaseWorkingSetPrivilege", false),
        })
        {
            if (TokenNative.LookupPrivilegeValue(null, name, out var luid))
                privs.Add((luid, enabled ? TokenNative.SE_PRIVILEGE_ENABLED : 0u));
        }
        return BuildTokenPrivilegesBuffer(privs);
    }

    // Builds the standard non-elevated user privilege set:
    //   SeChangeNotifyPrivilege (Enabled) + five disabled privileges present in every standard user token.
    private static IntPtr BuildStandardUserPrivilegesBuffer()
    {
        var privs = new List<(TokenNative.LUID, uint)>();
        foreach (var (name, enabled) in new (string, bool)[]
        {
            ("SeLockMemoryPrivilege",         false),
            ("SeShutdownPrivilege",           false),
            ("SeChangeNotifyPrivilege",       true),
            ("SeUndockPrivilege",             false),
            ("SeIncreaseWorkingSetPrivilege", false),
            ("SeTimeZonePrivilege",           false),
        })
        {
            if (TokenNative.LookupPrivilegeValue(null, name, out var luid))
                privs.Add((luid, enabled ? TokenNative.SE_PRIVILEGE_ENABLED : 0u));
        }
        return BuildTokenPrivilegesBuffer(privs);
    }

    // TOKEN_PRIVILEGES layout: DWORD PrivilegeCount (4 bytes) + N × LUID_AND_ATTRIBUTES (12 bytes each)
    // LUID_AND_ATTRIBUTES contains only DWORDs (no pointers), so layout is identical on 32-bit and 64-bit.
    private static IntPtr BuildTokenPrivilegesBuffer(IEnumerable<(TokenNative.LUID luid, uint attrs)> privs)
    {
        var list = privs.ToList();
        const int luidAttrSize = 12; // LUID(8: two DWORDs) + Attributes(4)
        int size = 4 + list.Count * luidAttrSize;
        IntPtr buf = Marshal.AllocHGlobal(size);
        Marshal.WriteInt32(buf, list.Count);
        int offset = 4;
        foreach (var (luid, attrs) in list)
        {
            Marshal.WriteInt32(buf, offset, (int)luid.LowPart);
            Marshal.WriteInt32(buf, offset + 4, luid.HighPart);
            Marshal.WriteInt32(buf, offset + 8, (int)attrs);
            offset += luidAttrSize;
        }
        return buf;
    }

    private static (IntPtr sidPtr, IntPtr rawBuf) GetTokenUserRaw(IntPtr hToken)
    {
        TokenNative.GetTokenInformation(hToken, TokenNative.TOKEN_INFORMATION_CLASS.TokenUser, IntPtr.Zero, 0, out uint needed);
        IntPtr buf = Marshal.AllocHGlobal((int)needed);
        if (!TokenNative.GetTokenInformation(hToken, TokenNative.TOKEN_INFORMATION_CLASS.TokenUser, buf, needed, out _))
            throw new InvalidOperationException($"GetTokenInformation(TokenUser) failed: {TokenHelper.GetLastError()}");
        return (Marshal.ReadIntPtr(buf), buf);
    }
}
