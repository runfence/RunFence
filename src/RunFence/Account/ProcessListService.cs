using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using RunFence.Acl;
using RunFence.Core;
using RunFence.Infrastructure;

namespace RunFence.Account;

public class ProcessListService(ILoggingService log, IProcessJobManager processJobManager) : IProcessListService
{
    private const uint ProcessQueryInformation = 0x0400;
    private const uint ProcessVmRead = 0x0010;

    public IReadOnlyList<ProcessInfo> GetProcessesForSid(string sid, CancellationToken cancellationToken = default)
    {
        var result = new List<ProcessInfo>();
        int tokenInfoClass = ProcessNative.GetTokenInfoClass(sid);
        // null = no filter (regular accounts); non-null = only show job members (SYSTEM / interactive user).
        // ?? [] means no job exists yet (no launches for this SID) → show nothing.
        HashSet<int>? allowedPids = SidResolutionHelper.NeedsProcessJobTracking(sid)
            ? (processJobManager.GetJobMembers(sid) ?? [])
            : null;

        foreach (var proc in Process.GetProcesses())
        {
            cancellationToken.ThrowIfCancellationRequested();
            using (proc)
            {
                if (proc.Id <= 4)
                    continue;
                try
                {
                    CollectProcess(proc.Id, sid, tokenInfoClass, allowedPids, result);
                }
                catch (Exception ex)
                {
                    log.Warn($"ProcessListService: failed to collect process {proc.Id}: {ex.Message}");
                }
            }
        }

        return result;
    }

    public HashSet<string> GetSidsWithProcesses(IEnumerable<string> sids, CancellationToken cancellationToken = default)
    {
        var sidSet = sids.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (sidSet.Count == 0)
            return found;

        var containerSids = sidSet
            .Where(s => AclHelper.IsContainerSid(s))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var regularSids = sidSet
            .Where(s => !AclHelper.IsContainerSid(s) && !AclHelper.IsLowIntegritySid(s))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var parentFilteredSids = regularSids
            .Where(SidResolutionHelper.NeedsProcessJobTracking)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Build job member sets once for all parent-filtered SIDs before the process loop.
        Dictionary<string, HashSet<int>>? jobMembersBySid = null;
        if (parentFilteredSids.Count > 0)
        {
            jobMembersBySid = new Dictionary<string, HashSet<int>>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in parentFilteredSids)
                jobMembersBySid[s] = processJobManager.GetJobMembers(s) ?? [];
        }

        var processes = Process.GetProcesses();
        try
        {
            foreach (var proc in processes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (proc.Id <= 4)
                    continue;
                if (found.Count == sidSet.Count)
                    break;

                IntPtr hProcess = ProcessNative.OpenProcess(ProcessNative.ProcessQueryLimitedInformation, false, proc.Id);
                if (hProcess == IntPtr.Zero)
                    continue;
                try
                {
                    if (HasExited(hProcess))
                        continue;

                    if (regularSids.Count > 0)
                    {
                        string? s = ProcessNative.GetTokenSid(hProcess, ProcessNative.TokenUser);
                        if (s != null && regularSids.Contains(s))
                        {
                            bool allowed = !parentFilteredSids.Contains(s) ||
                                (jobMembersBySid != null &&
                                 jobMembersBySid.TryGetValue(s, out var members) &&
                                 members.Contains(proc.Id));
                            if (allowed)
                                found.Add(s);
                        }
                    }

                    if (containerSids.Count > 0)
                    {
                        string? s = ProcessNative.GetTokenSid(hProcess, ProcessNative.TokenAppContainerSid);
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
                    ProcessNative.CloseHandle(hProcess);
                }
            }
        }
        finally
        {
            foreach (var proc in processes)
                proc.Dispose();
        }

        return found;
    }

    private static void CollectProcess(int pid, string sid, int tokenInfoClass, HashSet<int>? allowedPids, List<ProcessInfo> result)
    {
        if (allowedPids != null && !allowedPids.Contains(pid))
            return;

        using var hLimited = new ProcessNative.SafeNativeProcessHandle(
            ProcessNative.OpenProcess(ProcessNative.ProcessQueryLimitedInformation, false, pid));
        if (hLimited.IsInvalid)
            return;

        if (!string.Equals(ProcessNative.GetTokenSid(hLimited.DangerousGetHandle(), tokenInfoClass), sid, StringComparison.OrdinalIgnoreCase))
            return;

        if (HasExited(hLimited.DangerousGetHandle()))
            return;

        string? exePath;
        string? cmdLine = null;

        using var hFull = new ProcessNative.SafeNativeProcessHandle(
            ProcessNative.OpenProcess(ProcessQueryInformation | ProcessVmRead, false, pid));
        if (!hFull.IsInvalid)
        {
            exePath = GetExePath(hFull.DangerousGetHandle());
            cmdLine = GetCommandLine(hFull.DangerousGetHandle());
        }
        else
        {
            exePath = GetExePath(hLimited.DangerousGetHandle());
        }

        long? startTimeUtcTicks = null;
        if (ProcessNative.GetProcessTimes(
                hLimited.DangerousGetHandle(),
                out var creationTime,
                out _,
                out _,
                out _))
        {
            startTimeUtcTicks = DateTime.FromFileTimeUtc(creationTime.ToLong()).Ticks;
        }

        if (HasExited(hLimited.DangerousGetHandle()))
            return;

        result.Add(new ProcessInfo(pid, exePath, cmdLine, startTimeUtcTicks));
    }

    private static bool HasExited(IntPtr hProcess)
    {
        if (!ProcessNative.GetProcessTimes(
                hProcess,
                out _,
                out var exitTime,
                out _,
                out _))
        {
            return false;
        }

        return exitTime.ToLong() != 0;
    }

    private static string? GetExePath(IntPtr hProcess)
    {
        var buffer = new StringBuilder(1024);
        uint size = (uint)buffer.Capacity;
        return ProcessNative.QueryFullProcessImageName(hProcess, 0, buffer, ref size) ? buffer.ToString() : null;
    }

    private static string? GetCommandLine(IntPtr hProcess)
    {
        if (ProcessNative.IsWow64Process(hProcess, out bool isWow64) && isWow64)
            return null;

        var pbi = new ProcessNative.ProcessBasicInformation();
        if (ProcessNative.NtQueryInformationProcess(hProcess, 0, ref pbi,
                (uint)Marshal.SizeOf<ProcessNative.ProcessBasicInformation>(), out _) != 0)
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
            if (!ProcessNative.ReadProcessMemory(hProcess, cmdLineBuffer, buf, cmdLineLength, out var bytesRead))
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
            return ProcessNative.ReadProcessMemory(hProcess, address, buf, IntPtr.Size, out _)
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
            return ProcessNative.ReadProcessMemory(hProcess, address, buf, 2, out _)
                ? (ushort)Marshal.ReadInt16(buf)
                : (ushort)0;
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
    }
}
