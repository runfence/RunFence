using System.Runtime.InteropServices;
using RunFence.TokenTest.Native;

namespace RunFence.TokenTest;

internal class SystemTokenHelper
{
    private const string SystemSid = "S-1-5-18";

    public bool EnablePrivilege(string privilegeName)
    {
        if (!TokenNative.OpenProcessToken(ProcessNative.GetCurrentProcess(),
                TokenNative.TOKEN_ADJUST_PRIVILEGES | TokenNative.TOKEN_QUERY,
                out var hToken))
            return false;

        try
        {
            if (!TokenNative.LookupPrivilegeValue(null, privilegeName, out var luid))
                return false;

            var tp = new TokenNative.TOKEN_PRIVILEGES
            {
                PrivilegeCount = 1,
                Privileges = new[] { new TokenNative.LUID_AND_ATTRIBUTES { Luid = luid, Attributes = TokenNative.SE_PRIVILEGE_ENABLED } }
            };
            TokenNative.AdjustTokenPrivileges(hToken, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
            // AdjustTokenPrivileges returns true even when not all privileges could be set;
            // must check GetLastError to confirm all requested privileges were actually enabled.
            return Marshal.GetLastWin32Error() == 0;
        }
        finally
        {
            ProcessNative.CloseHandle(hToken);
        }
    }

    public bool EnableDebugPrivilege() => EnablePrivilege("SeDebugPrivilege");

    public IntPtr GetSystemToken()
    {
        // Prefer winlogon.exe; fall back to any accessible SYSTEM process
        IntPtr hToken = GetTokenByProcessName("winlogon.exe");
        if (hToken != IntPtr.Zero) return hToken;
        return GetAnySystemToken();
    }

    public IntPtr GetLsassToken() => GetTokenByProcessName("lsass.exe");

    private IntPtr GetTokenByProcessName(string targetName)
    {
        IntPtr hSnap = ProcessNative.CreateToolhelp32Snapshot(ProcessNative.TH32CS_SNAPPROCESS, 0);
        if (hSnap == IntPtr.Zero || hSnap == new IntPtr(-1)) return IntPtr.Zero;

        try
        {
            var entry = new ProcessNative.PROCESSENTRY32 { dwSize = (uint)Marshal.SizeOf<ProcessNative.PROCESSENTRY32>() };
            if (!ProcessNative.Process32First(hSnap, ref entry)) return IntPtr.Zero;

            do
            {
                if (!string.Equals(entry.szExeFile, targetName, StringComparison.OrdinalIgnoreCase))
                    continue;

                IntPtr hToken = TryGetProcessToken(entry.th32ProcessID);
                if (hToken != IntPtr.Zero) return hToken;
            } while (ProcessNative.Process32Next(hSnap, ref entry));
        }
        finally
        {
            ProcessNative.CloseHandle(hSnap);
        }
        return IntPtr.Zero;
    }

    private IntPtr GetAnySystemToken()
    {
        IntPtr hSnap = ProcessNative.CreateToolhelp32Snapshot(ProcessNative.TH32CS_SNAPPROCESS, 0);
        if (hSnap == IntPtr.Zero || hSnap == new IntPtr(-1)) return IntPtr.Zero;

        try
        {
            var entry = new ProcessNative.PROCESSENTRY32 { dwSize = (uint)Marshal.SizeOf<ProcessNative.PROCESSENTRY32>() };
            if (!ProcessNative.Process32First(hSnap, ref entry)) return IntPtr.Zero;

            do
            {
                IntPtr hToken = TryGetProcessToken(entry.th32ProcessID);
                if (hToken == IntPtr.Zero) continue;

                if (IsSystemToken(hToken))
                    return hToken;

                ProcessNative.CloseHandle(hToken);
            } while (ProcessNative.Process32Next(hSnap, ref entry));
        }
        finally
        {
            ProcessNative.CloseHandle(hSnap);
        }
        return IntPtr.Zero;
    }

    // Tries to open a process and then its token with progressively reduced access rights.
    private static IntPtr TryGetProcessToken(uint pid)
    {
        IntPtr hProcess = TryOpenProcess(pid);
        if (hProcess == IntPtr.Zero) return IntPtr.Zero;

        try
        {
            return TryOpenToken(hProcess);
        }
        finally
        {
            ProcessNative.CloseHandle(hProcess);
        }
    }

    private static IntPtr TryOpenProcess(uint pid)
    {
        IntPtr h = ProcessNative.OpenProcess(ProcessNative.PROCESS_QUERY_INFORMATION, false, pid);
        if (h != IntPtr.Zero) return h;
        return ProcessNative.OpenProcess(ProcessNative.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
    }

    private static IntPtr TryOpenToken(IntPtr hProcess)
    {
        if (TokenNative.OpenProcessToken(hProcess, TokenNative.TOKEN_ALL_ACCESS, out var h)) return h;
        if (TokenNative.OpenProcessToken(hProcess,
                TokenNative.TOKEN_DUPLICATE | TokenNative.TOKEN_QUERY | TokenNative.TOKEN_IMPERSONATE,
                out h)) return h;
        return IntPtr.Zero;
    }

    private static bool IsSystemToken(IntPtr hToken)
    {
        TokenNative.GetTokenInformation(hToken, TokenNative.TOKEN_INFORMATION_CLASS.TokenUser,
            IntPtr.Zero, 0, out uint needed);
        if (needed == 0) return false;

        IntPtr buf = Marshal.AllocHGlobal((int)needed);
        try
        {
            if (!TokenNative.GetTokenInformation(hToken, TokenNative.TOKEN_INFORMATION_CLASS.TokenUser, buf, needed, out _))
                return false;
            IntPtr sidPtr = Marshal.ReadIntPtr(buf);
            SecurityNative.ConvertSidToStringSid(sidPtr, out var sidStr);
            return sidStr == SystemSid;
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
    }

    public bool ImpersonateToken(IntPtr hToken)
    {
        if (!TokenNative.DuplicateTokenEx(hToken,
                TokenNative.TOKEN_IMPERSONATE | TokenNative.TOKEN_QUERY,
                IntPtr.Zero,
                TokenNative.SECURITY_IMPERSONATION_LEVEL.SecurityImpersonation,
                TokenNative.TOKEN_TYPE.TokenImpersonation,
                out var hImpToken))
            return false;

        try
        {
            return TokenNative.ImpersonateLoggedOnUser(hImpToken);
        }
        finally
        {
            ProcessNative.CloseHandle(hImpToken);
        }
    }

    public void RevertImpersonation() => TokenNative.RevertToSelf();
}
