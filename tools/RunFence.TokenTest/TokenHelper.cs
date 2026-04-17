using System.Runtime.InteropServices;
using System.Text;
using RunFence.TokenTest.Native;

namespace RunFence.TokenTest;

internal class TokenHelper
{
    public IntPtr OpenCurrentProcessToken()
    {
        if (!TokenNative.OpenProcessToken(ProcessNative.GetCurrentProcess(), TokenNative.TOKEN_ALL_ACCESS, out var hToken))
            throw new InvalidOperationException($"OpenProcessToken failed: {GetLastError()}");
        return hToken;
    }

    public IntPtr GetLogonSid(IntPtr hToken)
    {
        using var buf = GetTokenInfoBuffer(hToken, TokenNative.TOKEN_INFORMATION_CLASS.TokenGroups);
        IntPtr ptr = buf.DangerousGetHandle();
        uint groupCount = (uint)Marshal.ReadInt32(ptr);
        int offset = IntPtr.Size == 8 ? 8 : 4;
        for (uint i = 0; i < groupCount; i++)
        {
            IntPtr sidPtr = Marshal.ReadIntPtr(ptr, offset);
            uint attrs = (uint)Marshal.ReadInt32(ptr, offset + IntPtr.Size);
            if ((attrs & TokenNative.SE_GROUP_LOGON_ID) == TokenNative.SE_GROUP_LOGON_ID)
                return CopySid(sidPtr);
            offset += SidAndAttributesSize();
        }
        throw new InvalidOperationException("Logon SID not found in token groups");
    }

    public IntPtr GetUserSid(IntPtr hToken)
    {
        using var buf = GetTokenInfoBuffer(hToken, TokenNative.TOKEN_INFORMATION_CLASS.TokenUser);
        return CopySid(Marshal.ReadIntPtr(buf.DangerousGetHandle()));
    }

    public IntPtr GetAdminsSid()
    {
        uint cbSid = 256;
        IntPtr pSid = Marshal.AllocHGlobal((int)cbSid);
        if (!SecurityNative.CreateWellKnownSid(SecurityNative.WELL_KNOWN_SID_TYPE.WinBuiltinAdministratorsSid, IntPtr.Zero, pSid, ref cbSid))
            throw new InvalidOperationException($"CreateWellKnownSid(Admins) failed: {GetLastError()}");
        return pSid;
    }

    public IntPtr GetMediumIntegritySid()
    {
        uint cbSid = 256;
        IntPtr pSid = Marshal.AllocHGlobal((int)cbSid);
        if (!SecurityNative.CreateWellKnownSid(SecurityNative.WELL_KNOWN_SID_TYPE.WinMediumLabelSid, IntPtr.Zero, pSid, ref cbSid))
            throw new InvalidOperationException($"CreateWellKnownSid(MediumLabel) failed: {GetLastError()}");
        return pSid;
    }

    public IntPtr DuplicateAsPrimary(IntPtr hToken)
    {
        if (!TokenNative.DuplicateTokenEx(hToken, TokenNative.TOKEN_ALL_ACCESS, IntPtr.Zero,
                TokenNative.SECURITY_IMPERSONATION_LEVEL.SecurityImpersonation,
                TokenNative.TOKEN_TYPE.TokenPrimary,
                out var hNew))
            throw new InvalidOperationException($"DuplicateTokenEx failed: {GetLastError()}");
        return hNew;
    }

    public IntPtr CreateRestrictedNoAdmins(IntPtr hToken, IntPtr adminsSid)
    {
        var sidsToDisable = new TokenNative.SID_AND_ATTRIBUTES[] { new() { Sid = adminsSid, Attributes = 0 } };
        if (!TokenNative.CreateRestrictedToken(hToken, 0, 1, sidsToDisable, 0, IntPtr.Zero, 0, null, out var hRestricted))
            throw new InvalidOperationException($"CreateRestrictedToken failed: {GetLastError()}");
        return hRestricted;
    }

    // Creates a properly-structured restricted token by BOTH disabling Admins AND adding restricting SIDs.
    // Without restricting SIDs, csrss/kernel rejects the token during child-process DLL init (0xC0000142).
    // The restricting SID list is the intersection check: access is granted only if BOTH the main groups
    // AND the restricted groups grant it. Using the user + standard non-admin groups as restricting SIDs
    // blocks admin-only resources while preserving full standard-user access.
    public IntPtr CreateRestrictedWithSids(IntPtr hToken, IntPtr adminsSid, IntPtr userSid)
    {
        IntPtr usersSid = IntPtr.Zero, everyoneSid = IntPtr.Zero, authUsersSid = IntPtr.Zero;
        try
        {
            usersSid = AllocWellKnownSid(SecurityNative.WELL_KNOWN_SID_TYPE.WinBuiltinUsersSid);
            everyoneSid = AllocWellKnownSid(SecurityNative.WELL_KNOWN_SID_TYPE.WinWorldSid);
            authUsersSid = AllocWellKnownSid(SecurityNative.WELL_KNOWN_SID_TYPE.WinAuthenticatedUserSid);

            var sidsToDisable = new TokenNative.SID_AND_ATTRIBUTES[] { new() { Sid = adminsSid, Attributes = 0 } };
            var sidsToRestrict = new TokenNative.SID_AND_ATTRIBUTES[]
            {
                new() { Sid = userSid,      Attributes = 0 },
                new() { Sid = usersSid,     Attributes = 0 },
                new() { Sid = everyoneSid,  Attributes = 0 },
                new() { Sid = authUsersSid, Attributes = 0 },
            };

            if (!TokenNative.CreateRestrictedToken(hToken, 0, 1, sidsToDisable,
                    0, IntPtr.Zero, (uint)sidsToRestrict.Length, sidsToRestrict, out var hRestricted))
                throw new InvalidOperationException($"CreateRestrictedToken (with SidsToRestrict) failed: {GetLastError()}");
            return hRestricted;
        }
        finally
        {
            if (usersSid != IntPtr.Zero) Marshal.FreeHGlobal(usersSid);
            if (everyoneSid != IntPtr.Zero) Marshal.FreeHGlobal(everyoneSid);
            if (authUsersSid != IntPtr.Zero) Marshal.FreeHGlobal(authUsersSid);
        }
    }

    private static IntPtr AllocWellKnownSid(SecurityNative.WELL_KNOWN_SID_TYPE type)
    {
        uint cb = 256;
        IntPtr p = Marshal.AllocHGlobal((int)cb);
        if (!SecurityNative.CreateWellKnownSid(type, IntPtr.Zero, p, ref cb))
            throw new InvalidOperationException($"CreateWellKnownSid({type}) failed: {GetLastError()}");
        return p;
    }

    public bool SetMediumIntegrityLevel(IntPtr hToken)
    {
        IntPtr mediumSid = GetMediumIntegritySid();
        try
        {
            SetMediumIntegrity(hToken, mediumSid);
            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(mediumSid);
        }
    }

    public void SetMediumIntegrity(IntPtr hToken, IntPtr mediumSid)
    {
        var label = new TokenNative.TOKEN_MANDATORY_LABEL
        {
            Label = new TokenNative.SID_AND_ATTRIBUTES
            {
                Sid = mediumSid,
                Attributes = TokenNative.SE_GROUP_INTEGRITY
            }
        };
        int size = Marshal.SizeOf<TokenNative.TOKEN_MANDATORY_LABEL>();
        IntPtr buf = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(label, buf, false);
            if (!TokenNative.SetTokenInformation(hToken, TokenNative.TOKEN_INFORMATION_CLASS.TokenIntegrityLevel, buf, (uint)size))
                throw new InvalidOperationException($"SetTokenInformation(IntegrityLevel) failed: {GetLastError()}");
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
    }

    public void PrintTokenInfo(IntPtr hToken)
    {
        int elevType = 0;
        IntPtr etBuf = Marshal.AllocHGlobal(4);
        try
        {
            if (TokenNative.GetTokenInformation(hToken, TokenNative.TOKEN_INFORMATION_CLASS.TokenElevationType, etBuf, 4, out _))
                elevType = Marshal.ReadInt32(etBuf);
        }
        finally { Marshal.FreeHGlobal(etBuf); }

        string ilName = "?";
        using (var ilBuf = GetTokenInfoBuffer(hToken, TokenNative.TOKEN_INFORMATION_CLASS.TokenIntegrityLevel))
        {
            IntPtr sidPtr = Marshal.ReadIntPtr(ilBuf.DangerousGetHandle());
            if (SecurityNative.ConvertSidToStringSid(sidPtr, out var sidStr))
            {
                ilName = sidStr switch
                {
                    "S-1-16-4096" => "Low",
                    "S-1-16-8192" => "Medium",
                    "S-1-16-12288" => "High",
                    "S-1-16-16384" => "System",
                    _ => sidStr
                };
            }
        }

        int groupCount;
        using (var grpBuf = GetTokenInfoBuffer(hToken, TokenNative.TOKEN_INFORMATION_CLASS.TokenGroups))
            groupCount = Marshal.ReadInt32(grpBuf.DangerousGetHandle());

        int restrictedCount = 0;
        using (var rsBuf = GetTokenInfoBuffer(hToken, TokenNative.TOKEN_INFORMATION_CLASS.TokenRestrictedSids))
            restrictedCount = Marshal.ReadInt32(rsBuf.DangerousGetHandle());

        int privCount = 0;
        using (var privBuf = GetTokenInfoBuffer(hToken, TokenNative.TOKEN_INFORMATION_CLASS.TokenPrivileges))
            privCount = Marshal.ReadInt32(privBuf.DangerousGetHandle());

        Console.WriteLine($"  Step (info): ElevationType={elevType} IL={ilName} Groups={groupCount} RestrictedSids={restrictedCount} Privs={privCount}");
    }

    public TokenNative.TOKEN_STATISTICS GetTokenStatistics(IntPtr hToken)
    {
        int size = Marshal.SizeOf<TokenNative.TOKEN_STATISTICS>();
        IntPtr buf = Marshal.AllocHGlobal(size);
        try
        {
            if (!TokenNative.GetTokenInformation(hToken, TokenNative.TOKEN_INFORMATION_CLASS.TokenStatistics, buf, (uint)size, out _))
                throw new InvalidOperationException($"GetTokenInformation(TokenStatistics) failed: {GetLastError()}");
            return Marshal.PtrToStructure<TokenNative.TOKEN_STATISTICS>(buf);
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    // Returns the raw token info buffer (caller must free) + parsed group list with SID ptrs into that buffer.
    public (IntPtr rawBuf, List<(IntPtr sid, uint attrs)> groups) GetTokenGroups(IntPtr hToken)
    {
        var safeBuf = GetTokenInfoBuffer(hToken, TokenNative.TOKEN_INFORMATION_CLASS.TokenGroups);
        IntPtr ptr = safeBuf.DangerousGetHandle();
        safeBuf.SetHandleAsInvalid(); // transfer ownership to caller

        int groupCount = Marshal.ReadInt32(ptr);
        var groups = new List<(IntPtr sid, uint attrs)>(groupCount);
        int offset = IntPtr.Size == 8 ? 8 : 4;
        for (int i = 0; i < groupCount; i++)
        {
            groups.Add((Marshal.ReadIntPtr(ptr, offset), (uint)Marshal.ReadInt32(ptr, offset + IntPtr.Size)));
            offset += SidAndAttributesSize();
        }
        return (ptr, groups);
    }

    // Returns SID pointer and the raw buffer containing it. Caller must free rawBuf after use.
    public (IntPtr sidPtr, IntPtr rawBuf) GetTokenOwner(IntPtr hToken)
    {
        var safeBuf = GetTokenInfoBuffer(hToken, TokenNative.TOKEN_INFORMATION_CLASS.TokenOwner);
        IntPtr ptr = safeBuf.DangerousGetHandle();
        safeBuf.SetHandleAsInvalid();
        return (Marshal.ReadIntPtr(ptr), ptr);
    }

    // Returns SID pointer and the raw buffer containing it. Caller must free rawBuf after use.
    public (IntPtr sidPtr, IntPtr rawBuf) GetTokenPrimaryGroup(IntPtr hToken)
    {
        var safeBuf = GetTokenInfoBuffer(hToken, TokenNative.TOKEN_INFORMATION_CLASS.TokenPrimaryGroup);
        IntPtr ptr = safeBuf.DangerousGetHandle();
        safeBuf.SetHandleAsInvalid();
        return (Marshal.ReadIntPtr(ptr), ptr);
    }

    // Returns DACL pointer (into rawBuf) and rawBuf. Caller must free rawBuf after use.
    public (IntPtr dacl, IntPtr rawBuf) GetTokenDefaultDacl(IntPtr hToken)
    {
        var safeBuf = GetTokenInfoBuffer(hToken, TokenNative.TOKEN_INFORMATION_CLASS.TokenDefaultDacl);
        IntPtr ptr = safeBuf.DangerousGetHandle();
        safeBuf.SetHandleAsInvalid();
        return (Marshal.ReadIntPtr(ptr), ptr);
    }

    public IntPtr FindSameUserMediumToken(IntPtr hCurrentToken)
    {
        using var userBuf = GetTokenInfoBuffer(hCurrentToken, TokenNative.TOKEN_INFORMATION_CLASS.TokenUser);
        SecurityNative.ConvertSidToStringSid(Marshal.ReadIntPtr(userBuf.DangerousGetHandle()), out var currentUserSid);
        if (currentUserSid == null) return IntPtr.Zero;

        uint currentPid = ProcessNative.GetCurrentProcessId();
        IntPtr hSnap = ProcessNative.CreateToolhelp32Snapshot(ProcessNative.TH32CS_SNAPPROCESS, 0);
        if (hSnap == IntPtr.Zero || hSnap == new IntPtr(-1)) return IntPtr.Zero;
        try
        {
            var entry = new ProcessNative.PROCESSENTRY32 { dwSize = (uint)Marshal.SizeOf<ProcessNative.PROCESSENTRY32>() };
            if (!ProcessNative.Process32First(hSnap, ref entry)) return IntPtr.Zero;
            do
            {
                if (entry.th32ProcessID == currentPid) continue;
                IntPtr found = TryGetCandidateToken(entry.th32ProcessID, currentUserSid);
                if (found != IntPtr.Zero) return found;
            } while (ProcessNative.Process32Next(hSnap, ref entry));
        }
        finally { ProcessNative.CloseHandle(hSnap); }
        return IntPtr.Zero;
    }

    private IntPtr TryGetCandidateToken(uint pid, string targetUserSid)
    {
        IntPtr hProcess = ProcessNative.OpenProcess(ProcessNative.PROCESS_QUERY_INFORMATION, false, pid);
        if (hProcess == IntPtr.Zero)
            hProcess = ProcessNative.OpenProcess(ProcessNative.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (hProcess == IntPtr.Zero) return IntPtr.Zero;
        try
        {
            if (!TokenNative.OpenProcessToken(hProcess, TokenNative.TOKEN_QUERY | TokenNative.TOKEN_DUPLICATE, out var hTok))
                return IntPtr.Zero;
            try
            {
                if (!MatchesUserAndMediumIL(hTok, targetUserSid)) return IntPtr.Zero;
                return TokenNative.DuplicateTokenEx(hTok, TokenNative.TOKEN_ALL_ACCESS, IntPtr.Zero,
                    TokenNative.SECURITY_IMPERSONATION_LEVEL.SecurityImpersonation,
                    TokenNative.TOKEN_TYPE.TokenPrimary, out var hDup) ? hDup : IntPtr.Zero;
            }
            finally { ProcessNative.CloseHandle(hTok); }
        }
        finally { ProcessNative.CloseHandle(hProcess); }
    }

    private static bool MatchesUserAndMediumIL(IntPtr hToken, string targetUserSid)
    {
        // Check user SID
        TokenNative.GetTokenInformation(hToken, TokenNative.TOKEN_INFORMATION_CLASS.TokenUser,
            IntPtr.Zero, 0, out uint needed);
        if (needed == 0) return false;
        IntPtr buf = Marshal.AllocHGlobal((int)needed);
        try
        {
            if (!TokenNative.GetTokenInformation(hToken, TokenNative.TOKEN_INFORMATION_CLASS.TokenUser,
                    buf, needed, out _)) return false;
            SecurityNative.ConvertSidToStringSid(Marshal.ReadIntPtr(buf), out var sid);
            if (sid != targetUserSid) return false;
        }
        finally { Marshal.FreeHGlobal(buf); }

        // Check IL == Medium (S-1-16-8192)
        TokenNative.GetTokenInformation(hToken, TokenNative.TOKEN_INFORMATION_CLASS.TokenIntegrityLevel,
            IntPtr.Zero, 0, out needed);
        if (needed == 0) return false;
        buf = Marshal.AllocHGlobal((int)needed);
        try
        {
            if (!TokenNative.GetTokenInformation(hToken, TokenNative.TOKEN_INFORMATION_CLASS.TokenIntegrityLevel,
                    buf, needed, out _)) return false;
            SecurityNative.ConvertSidToStringSid(Marshal.ReadIntPtr(buf), out var ilSid);
            return ilSid == "S-1-16-8192";
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    public static IntPtr CopySid(IntPtr pSid)
    {
        int len = SecurityNative.GetLengthSid(pSid);
        IntPtr copy = Marshal.AllocHGlobal(len);
        if (!SecurityNative.CopySid((uint)len, copy, pSid))
            throw new InvalidOperationException($"CopySid failed: {GetLastError()}");
        return copy;
    }

    public static string GetLastError() => FormatError((uint)Marshal.GetLastWin32Error());

    public static string FormatError(uint code)
    {
        var sb = new StringBuilder(512);
        ProcessNative.FormatMessage(
            ProcessNative.FORMAT_MESSAGE_FROM_SYSTEM | ProcessNative.FORMAT_MESSAGE_IGNORE_INSERTS,
            IntPtr.Zero, code, 0, sb, (uint)sb.Capacity, IntPtr.Zero);
        string msg = sb.ToString().Trim();
        return msg.Length > 0 ? $"{code} ({msg})" : $"{code}";
    }

    private static HGlobalSafeHandle GetTokenInfoBuffer(IntPtr hToken, TokenNative.TOKEN_INFORMATION_CLASS cls)
    {
        TokenNative.GetTokenInformation(hToken, cls, IntPtr.Zero, 0, out uint needed);
        IntPtr ptr = Marshal.AllocHGlobal((int)needed);
        var buf = new HGlobalSafeHandle(ptr);
        if (!TokenNative.GetTokenInformation(hToken, cls, ptr, needed, out _))
            throw new InvalidOperationException($"GetTokenInformation({cls}) failed: {GetLastError()}");
        return buf;
    }

    // Returns the byte stride of SID_AND_ATTRIBUTES in a TOKEN_GROUPS array.
    private static int SidAndAttributesSize() =>
        IntPtr.Size == 8 ? 16 : 8; // IntPtr(8) + uint(4) + pad(4) on 64-bit; IntPtr(4) + uint(4) on 32-bit
}
