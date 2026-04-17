using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace RunFence.Infrastructure;

/// <summary>
/// P/Invoke declarations for process, thread, and token management APIs.
/// Consumed by process enumeration, launch, and token inspection services.
/// </summary>
public static class ProcessNative
{
    // ── Kernel32 ───────────────────────────────────────────────────────────────

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll")]
    public static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr LocalFree(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern SafeProcessHandle OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool QueryFullProcessImageName(IntPtr hProcess, uint dwFlags,
        StringBuilder lpExeName, ref uint lpdwSize);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool QueryFullProcessImageName(SafeProcessHandle hProcess, uint dwFlags,
        StringBuilder lpExeName, ref uint lpdwSize);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool QueryFullProcessImageName(IntPtr hProcess, uint dwFlags,
        StringBuilder lpExeName, ref int lpdwSize);

    [DllImport("kernel32.dll")]
    public static extern bool GetNamedPipeClientProcessId(IntPtr pipe, out uint clientProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress,
        IntPtr lpBuffer, IntPtr nSize, out IntPtr lpNumberOfBytesRead);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool IsWow64Process(IntPtr hProcess, out bool wow64Process);

    // ── Ntdll ──────────────────────────────────────────────────────────────────

    [DllImport("ntdll.dll")]
    public static extern uint NtQueryInformationProcess(IntPtr ProcessHandle,
        uint ProcessInformationClass, ref ProcessBasicInformation ProcessInformation,
        uint ProcessInformationLength, out uint ReturnLength);

    [StructLayout(LayoutKind.Sequential)]
    public struct ProcessBasicInformation
    {
        public IntPtr Reserved1;
        public IntPtr PebBaseAddress;
        public IntPtr Reserved2;
        public IntPtr Reserved3;
        public IntPtr UniqueProcessId;
        public IntPtr Reserved4;
    }

    // ── Advapi32 ───────────────────────────────────────────────────────────────

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool ImpersonateLoggedOnUser(IntPtr hToken);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool RevertToSelf();

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool OpenProcessToken(IntPtr ProcessHandle,
        uint DesiredAccess, out IntPtr TokenHandle);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool ConvertStringSidToSid(string StringSid, out IntPtr Sid);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool GetTokenInformation(IntPtr TokenHandle, int TokenInformationClass,
        IntPtr TokenInformation, uint TokenInformationLength, out uint ReturnLength);

    // ── Shared token helpers ───────────────────────────────────────────────────

    private const uint TokenQuery = 0x0008;

    /// <summary>Token information class for the user SID (TOKEN_USER).</summary>
    public const int TokenUser = 1;

    /// <summary>Token information class for the AppContainer SID (TOKEN_APPCONTAINER_INFORMATION).</summary>
    public const int TokenAppContainerSid = 31;

    /// <summary>Desired access for process handle to query tokens.</summary>
    public const uint ProcessQueryLimitedInformation = 0x1000;

    /// <summary>
    /// Returns the token information class to use for SID lookup based on the SID prefix.
    /// AppContainer SIDs start with "S-1-15-2-"; all other SIDs use TOKEN_USER.
    /// </summary>
    public static int GetTokenInfoClass(string sid) =>
        sid.StartsWith("S-1-15-2-", StringComparison.OrdinalIgnoreCase)
            ? TokenAppContainerSid
            : TokenUser;

    /// <summary>
    /// Reads the SID from a process token using the specified token information class.
    /// TOKEN_USER and TOKEN_APPCONTAINER_INFORMATION share the same leading layout:
    /// a pointer to the SID as the first field.
    /// Returns null on any error (best-effort, used for matching only).
    /// </summary>
    public static string? GetTokenSid(IntPtr hProcess, int tokenInfoClass)
    {
        if (!OpenProcessToken(hProcess, TokenQuery, out var hToken))
            return null;
        try
        {
            GetTokenInformation(hToken, tokenInfoClass, IntPtr.Zero, 0, out var needed);
            if (needed == 0)
                return null;

            var buffer = Marshal.AllocHGlobal((int)needed);
            try
            {
                if (!GetTokenInformation(hToken, tokenInfoClass, buffer, needed, out _))
                    return null;
                var sidPtr = Marshal.ReadIntPtr(buffer);
                if (sidPtr == IntPtr.Zero)
                    return null;
                return new SecurityIdentifier(sidPtr).Value;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        finally
        {
            CloseHandle(hToken);
        }
    }
}
