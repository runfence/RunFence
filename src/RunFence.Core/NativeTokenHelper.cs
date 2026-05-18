using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace RunFence.Core;

// P/Invoke duplication with higher-level native helpers is architecturally justified:
// RunFence.Core is a foundation-level project that cannot reference RunFence or
// RunFence.Infrastructure, so token/process P/Invokes required here must be declared locally.
public static class NativeTokenHelper
{
    public const int MandatoryLevelLow = 0x1000;
    public const int MandatoryLevelMedium = 0x2000;
    public const int MandatoryLevelHigh = 0x3000;

    private const int TOKEN_QUERY = 0x0008;
    private const int TOKEN_USER = 1;
    private const int TOKEN_APPCONTAINER_SID = 31;
    private const int TOKEN_INTEGRITY_LEVEL = 25;
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private const uint PROCESS_QUERY_INFORMATION = 0x0400;

    /// <summary>
    /// Returns processes with the given name from the current Windows session only.
    /// Caller owns the returned <see cref="Process"/> instances and must dispose them.
    /// </summary>
    public static IReadOnlyList<Process> GetProcessesByNameInCurrentSession(string processName)
    {
        var currentSessionId = Process.GetCurrentProcess().SessionId;
        var matches = new List<Process>();

        foreach (var process in Process.GetProcesses())
        {
            var keep = false;
            try
            {
                if (process.SessionId != currentSessionId)
                    continue;
                if (!string.Equals(process.ProcessName, processName, StringComparison.OrdinalIgnoreCase))
                    continue;

                matches.Add(process);
                keep = true;
            }
            catch
            {
                // Skip inaccessible processes.
            }
            finally
            {
                if (!keep)
                    process.Dispose();
            }
        }

        return matches;
    }

    /// <summary>
    /// Returns the SID of the interactive user in the current session by inspecting
    /// the owner of explorer.exe. Returns null if not determinable.
    /// </summary>
    public static SecurityIdentifier? TryGetInteractiveUserSid()
    {
        try
        {
            var explorers = GetProcessesByNameInCurrentSession("explorer");
            try
            {
                foreach (var proc in explorers)
                {
                    try
                    {
                        var sid = TryGetProcessOwnerSid(proc.Handle);
                        if (sid != null)
                            return sid;
                    }
                    catch
                    {
                        /* skip inaccessible process */
                    }
                }
            }
            finally
            {
                foreach (var p in explorers)
                    p.Dispose();
            }
        }
        catch
        {
            /* fall through */
        }

        return null;
    }

    /// <summary>
    /// Opens the winlogon.exe process in the current session and returns the process handle.
    /// Caller must close the handle via CloseHandle.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when winlogon.exe is not found in the current session.</exception>
    /// <exception cref="System.ComponentModel.Win32Exception">Thrown when OpenProcess fails.</exception>
    public static IntPtr OpenWinlogonProcess()
    {
        var currentSessionId = Process.GetCurrentProcess().SessionId;
        var winlogonProcesses = Process.GetProcessesByName("winlogon");
        int winlogonId;
        try
        {
            var winlogon = winlogonProcesses.FirstOrDefault(p => p.SessionId == currentSessionId);
            if (winlogon == null)
                throw new InvalidOperationException("winlogon.exe not found in current session");
            winlogonId = winlogon.Id;
        }
        finally
        {
            foreach (var p in winlogonProcesses)
                p.Dispose();
        }

        var hProcess = OpenProcess(PROCESS_QUERY_INFORMATION, false, (uint)winlogonId);
        if (hProcess == IntPtr.Zero)
            throw new System.ComponentModel.Win32Exception(
                Marshal.GetLastWin32Error(),
                "OpenProcess(winlogon) failed");
        return hProcess;
    }

    /// <summary>
    /// Returns the SID of the owner of the specified process by PID.
    /// Opens the process with PROCESS_QUERY_LIMITED_INFORMATION and closes it internally.
    /// Returns null if the process is inaccessible or SID cannot be determined.
    /// </summary>
    public static SecurityIdentifier? TryGetProcessOwnerSid(uint processId)
    {
        var handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (handle == IntPtr.Zero)
            return null;
        try
        {
            return TryGetProcessOwnerSid(handle);
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    public static SecurityIdentifier? TryGetProcessAppContainerSid(uint processId)
    {
        var handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (handle == IntPtr.Zero)
            return null;
        try
        {
            return TryGetTokenSid(handle, TOKEN_APPCONTAINER_SID);
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    /// <summary>
    /// Returns the integrity level RID of the specified process (e.g. <see cref="MandatoryLevelLow"/>,
    /// <see cref="MandatoryLevelMedium"/>, <see cref="MandatoryLevelHigh"/>).
    /// Opens the process with PROCESS_QUERY_LIMITED_INFORMATION and closes it internally.
    /// Returns null if the process is inaccessible or IL cannot be determined.
    /// </summary>
    public static int? TryGetProcessIntegrityLevel(uint processId)
    {
        var handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (handle == IntPtr.Zero)
            return null;
        try
        {
            return TryGetProcessIntegrityLevel(handle);
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    private static int? TryGetProcessIntegrityLevel(IntPtr processHandle)
    {
        if (!OpenProcessToken(processHandle, TOKEN_QUERY, out var tokenHandle))
            return null;
        try
        {
            GetTokenInformation(tokenHandle, TOKEN_INTEGRITY_LEVEL, IntPtr.Zero, 0, out var needed);
            if (needed <= 0)
                return null;
            var buffer = Marshal.AllocHGlobal(needed);
            try
            {
                if (!GetTokenInformation(tokenHandle, TOKEN_INTEGRITY_LEVEL, buffer, needed, out _))
                    return null;
                // TOKEN_MANDATORY_LABEL { SID_AND_ATTRIBUTES Label { IntPtr Sid; uint Attributes } }
                var sidPtr = Marshal.ReadIntPtr(buffer);
                var sid = new SecurityIdentifier(sidPtr);
                var binary = new byte[sid.BinaryLength];
                sid.GetBinaryForm(binary, 0);
                // RID is the last 4 bytes of the SID binary form (little-endian)
                return binary[^4]
                       | (binary[^3] << 8)
                       | (binary[^2] << 16)
                       | (binary[^1] << 24);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        finally
        {
            CloseHandle(tokenHandle);
        }
    }

    public static SecurityIdentifier? TryGetProcessOwnerSid(IntPtr processHandle)
        => TryGetTokenSid(processHandle, TOKEN_USER);

    private static SecurityIdentifier? TryGetTokenSid(IntPtr processHandle, int tokenInformationClass)
    {
        if (!OpenProcessToken(processHandle, TOKEN_QUERY, out var tokenHandle))
            return null;

        try
        {
            GetTokenInformation(tokenHandle, tokenInformationClass, IntPtr.Zero, 0, out var needed);
            if (needed <= 0)
                return null;

            var buffer = Marshal.AllocHGlobal(needed);
            try
            {
                if (GetTokenInformation(tokenHandle, tokenInformationClass, buffer, needed, out _))
                {
                    var sidPtr = Marshal.ReadIntPtr(buffer);
                    if (sidPtr != IntPtr.Zero)
                        return new SecurityIdentifier(sidPtr);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        finally
        {
            CloseHandle(tokenHandle);
        }

        return null;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr processHandle, int desiredAccess, out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(IntPtr tokenHandle, int tokenInformationClass,
        IntPtr tokenInformation, int tokenInformationLength, out int returnLength);
}
