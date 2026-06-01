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
    public static extern uint GetProcessId(IntPtr process);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern SafeProcessHandle OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool QueryFullProcessImageName(IntPtr hProcess, uint dwFlags,
        StringBuilder lpExeName, ref uint lpdwSize);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool QueryFullProcessImageName(SafeProcessHandle hProcess, uint dwFlags,
        StringBuilder lpExeName, ref uint lpdwSize);

    [DllImport("kernel32.dll")]
    public static extern bool GetNamedPipeClientProcessId(IntPtr pipe, out uint clientProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress,
        IntPtr lpBuffer, IntPtr nSize, out IntPtr lpNumberOfBytesRead);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress,
        [Out] byte[] lpBuffer, IntPtr nSize, out IntPtr lpNumberOfBytesRead);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool IsWow64Process(IntPtr hProcess, out bool wow64Process);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool GetProcessTimes(
        IntPtr hProcess,
        out FileTimeStruct creationTime,
        out FileTimeStruct exitTime,
        out FileTimeStruct kernelTime,
        out FileTimeStruct userTime);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress,
        uint dwSize, uint flAllocationType, uint flProtect);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress,
        byte[] lpBuffer, IntPtr nSize, out IntPtr written);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr CreateRemoteThread(IntPtr hProcess, IntPtr lpThreadAttributes,
        uint dwStackSize, IntPtr lpStartAddress, IntPtr lpParameter,
        uint dwCreationFlags, out uint lpThreadId);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool VirtualFreeEx(IntPtr hProcess, IntPtr lpAddress,
        uint dwSize, uint dwFreeType);

    // ── Ntdll ──────────────────────────────────────────────────────────────────

    [DllImport("ntdll.dll")]
    public static extern uint NtQueryInformationProcess(IntPtr ProcessHandle,
        uint ProcessInformationClass, ref ProcessBasicInformation ProcessInformation,
        uint ProcessInformationLength, out uint ReturnLength);

    [DllImport("ntdll.dll")]
    public static extern uint NtQueryInformationProcess(IntPtr ProcessHandle,
        uint ProcessInformationClass, out IntPtr ProcessInformation,
        uint ProcessInformationLength, out uint ReturnLength);

    [StructLayout(LayoutKind.Sequential)]
    public struct ProcessBasicInformation
    {
        public IntPtr Reserved1;
        public IntPtr PebBaseAddress;
        public IntPtr Reserved2;
        public IntPtr Reserved3;
        public IntPtr UniqueProcessId;
        public IntPtr InheritedFromUniqueProcessId;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct FileTimeStruct
    {
        public uint DwLowDateTime;
        public uint DwHighDateTime;

        public long ToLong() => ((long)DwHighDateTime << 32) | DwLowDateTime;
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
    public const uint ProcessTerminate = 0x0001;
    public const uint ProcessDuplicateHandle = 0x0040;

    public const uint PROCESS_CREATE_THREAD     = 0x0002u;
    public const uint PROCESS_VM_OPERATION      = 0x0008u;
    public const uint PROCESS_VM_READ           = 0x0010u;
    public const uint PROCESS_VM_WRITE          = 0x0020u;
    public const uint PROCESS_QUERY_INFORMATION = 0x0400u;
    public const uint MEM_COMMIT                = 0x1000u;
    public const uint MEM_RESERVE               = 0x2000u;
    public const uint MEM_RELEASE               = 0x8000u;
    public const uint PAGE_READWRITE            = 0x04u;
    public const uint PAGE_EXECUTE_READWRITE    = 0x40u;

    /// <summary>
    /// Returns the token information class to use for SID lookup based on the SID prefix.
    /// AppContainer SIDs start with "S-1-15-2-"; all other SIDs use TOKEN_USER.
    /// </summary>
    public static int GetTokenInfoClass(string sid) =>
        sid.StartsWith("S-1-15-2-", StringComparison.OrdinalIgnoreCase)
            ? TokenAppContainerSid
            : TokenUser;

    /// <summary>
    /// A <see cref="SafeHandleZeroOrMinusOneIsInvalid"/> wrapper for native process handles
    /// opened via <see cref="OpenProcess(uint,bool,int)"/>. Use with <c>using var</c> to
    /// ensure <c>CloseHandle</c> is called even on exceptions.
    /// </summary>
    public sealed class SafeNativeProcessHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public SafeNativeProcessHandle(IntPtr handle) : base(ownsHandle: true)
        {
            SetHandle(handle);
        }

        protected override bool ReleaseHandle() => CloseHandle(handle);
    }

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
