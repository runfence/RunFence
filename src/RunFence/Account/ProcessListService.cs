using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using RunFence.Core;
using RunFence.Infrastructure;

namespace RunFence.Account;

public class ProcessListService(ILoggingService log) : IProcessListService
{
    private const uint ProcessQueryInformation = 0x0400;
    private const uint ProcessVmRead = 0x0010;

    public IReadOnlyList<ProcessInfo> GetProcessesForSid(string sid)
    {
        var result = new List<ProcessInfo>();
        int tokenInfoClass = NativeMethods.GetTokenInfoClass(sid);

        foreach (var proc in Process.GetProcesses())
        {
            using (proc)
            {
                if (proc.Id <= 4)
                    continue;
                try
                {
                    CollectProcess(proc.Id, sid, tokenInfoClass, result);
                }
                catch (Exception ex)
                {
                    log.Warn($"ProcessListService: failed to collect process {proc.Id}: {ex.Message}");
                }
            }
        }

        return result;
    }

    public HashSet<string> GetSidsWithProcesses(IEnumerable<string> sids)
    {
        var sidSet = sids.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (sidSet.Count == 0)
            return found;

        var containerSids = sidSet
            .Where(s => s.StartsWith("S-1-15-2-", StringComparison.OrdinalIgnoreCase))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var regularSids = sidSet
            .Where(s => !s.StartsWith("S-1-15-2-", StringComparison.OrdinalIgnoreCase))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var proc in Process.GetProcesses())
        {
            using (proc)
            {
                if (proc.Id <= 4)
                    continue;
                if (found.Count == sidSet.Count)
                    break;

                IntPtr hProcess = NativeMethods.OpenProcess(NativeMethods.ProcessQueryLimitedInformation, false, proc.Id);
                if (hProcess == IntPtr.Zero)
                    continue;
                try
                {
                    if (regularSids.Count > 0)
                    {
                        string? s = NativeMethods.GetTokenSid(hProcess, NativeMethods.TokenUser);
                        if (s != null && regularSids.Contains(s))
                            found.Add(s);
                    }

                    if (containerSids.Count > 0)
                    {
                        string? s = NativeMethods.GetTokenSid(hProcess, NativeMethods.TokenAppContainerSid);
                        if (s != null && containerSids.Contains(s))
                            found.Add(s);
                    }
                }
                catch (Exception ex)
                {
                    log.Warn($"ProcessListService: failed to query process {proc.Id}: {ex.Message}");
                }
                finally
                {
                    NativeMethods.CloseHandle(hProcess);
                }
            }
        }

        return found;
    }

    private static void CollectProcess(int pid, string sid, int tokenInfoClass, List<ProcessInfo> result)
    {
        IntPtr hLimited = NativeMethods.OpenProcess(NativeMethods.ProcessQueryLimitedInformation, false, pid);
        if (hLimited == IntPtr.Zero)
            return;
        try
        {
            if (!string.Equals(NativeMethods.GetTokenSid(hLimited, tokenInfoClass), sid, StringComparison.OrdinalIgnoreCase))
                return;
        }
        finally
        {
            NativeMethods.CloseHandle(hLimited);
        }

        string? exePath = null;
        string? cmdLine = null;

        IntPtr hFull = NativeMethods.OpenProcess(ProcessQueryInformation | ProcessVmRead, false, pid);
        if (hFull != IntPtr.Zero)
        {
            try
            {
                exePath = GetExePath(hFull);
                cmdLine = GetCommandLine(hFull);
            }
            finally
            {
                NativeMethods.CloseHandle(hFull);
            }
        }
        else
        {
            hLimited = NativeMethods.OpenProcess(NativeMethods.ProcessQueryLimitedInformation, false, pid);
            if (hLimited != IntPtr.Zero)
            {
                try
                {
                    exePath = GetExePath(hLimited);
                }
                finally
                {
                    NativeMethods.CloseHandle(hLimited);
                }
            }
        }

        result.Add(new ProcessInfo(pid, exePath, cmdLine));
    }

    private static string? GetExePath(IntPtr hProcess)
    {
        var buffer = new StringBuilder(1024);
        uint size = (uint)buffer.Capacity;
        return NativeMethods.QueryFullProcessImageName(hProcess, 0, buffer, ref size) ? buffer.ToString() : null;
    }

    private static string? GetCommandLine(IntPtr hProcess)
    {
        if (NativeMethods.IsWow64Process(hProcess, out bool isWow64) && isWow64)
            return null;

        var pbi = new NativeMethods.ProcessBasicInformation();
        if (NativeMethods.NtQueryInformationProcess(hProcess, 0, ref pbi,
                (uint)Marshal.SizeOf<NativeMethods.ProcessBasicInformation>(), out _) != 0)
            return null;

        if (pbi.PebBaseAddress == IntPtr.Zero)
            return null;

        // ProcessParameters pointer is at PEB offset 0x20 in 64-bit
        IntPtr processParams = ReadPtr(hProcess, pbi.PebBaseAddress + 0x20);
        if (processParams == IntPtr.Zero)
            return null;

        // CommandLine UNICODE_STRING: Length (USHORT) at +0x70, Buffer (pointer) at +0x78
        ushort cmdLineLength = ReadUShort(hProcess, processParams + 0x70);
        IntPtr cmdLineBuffer = ReadPtr(hProcess, processParams + 0x78);
        if (cmdLineLength == 0 || cmdLineBuffer == IntPtr.Zero)
            return null;

        var buf = Marshal.AllocHGlobal(cmdLineLength);
        try
        {
            if (!NativeMethods.ReadProcessMemory(hProcess, cmdLineBuffer, buf, cmdLineLength, out var bytesRead))
                return null;
            var bytes = new byte[(int)bytesRead];
            Marshal.Copy(buf, bytes, 0, (int)bytesRead);
            return Encoding.Unicode.GetString(bytes);
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
    }

    private static IntPtr ReadPtr(IntPtr hProcess, IntPtr address)
    {
        var buf = Marshal.AllocHGlobal(IntPtr.Size);
        try
        {
            return NativeMethods.ReadProcessMemory(hProcess, address, buf, IntPtr.Size, out _)
                ? Marshal.ReadIntPtr(buf)
                : IntPtr.Zero;
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
    }

    private static ushort ReadUShort(IntPtr hProcess, IntPtr address)
    {
        var buf = Marshal.AllocHGlobal(2);
        try
        {
            return NativeMethods.ReadProcessMemory(hProcess, address, buf, 2, out _)
                ? (ushort)Marshal.ReadInt16(buf)
                : (ushort)0;
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
    }
}