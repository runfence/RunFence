using System.Runtime.InteropServices;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Firewall.Wfp;
using RunFence.Infrastructure;
using Timer = System.Threading.Timer;

namespace RunFence.Firewall;

/// <summary>
/// Background service that periodically enumerates listening TCP/UDP endpoints, resolves each
/// to its owner SID, and dynamically blocks cross-user ephemeral ports (≥49152) that fall within
/// range exemptions in the account's LocalhostPortExemptions list. Runs on a 1-second timer.
/// </summary>
public class WfpEphemeralPortScanner(
    IWfpLocalhostBlocker wfpBlocker,
    UiThreadDatabaseAccessor db,
    ILoggingService log,
    bool startTimer = true)
    : IBackgroundService, IDisposable
{
    private const int EphemeralPortRangeStart = 49152;

    private record BlockedAccount(string Sid, List<PortRange> ExemptedRanges, bool FilterEphemeral);

    // PID→SID cache with 10-second TTL. Known trade-off: if a process exits and a new process reuses
    // its PID within the TTL window, the cached (old) SID is used instead of the new owner. This can
    // cause one scan cycle of incorrect blocking — the error self-corrects on the next cache miss after TTL expiry.
    // Accepted trade-off: PID reuse within 10 seconds is extremely rare, and the performance benefit
    // (avoiding an OpenProcessToken syscall per observed port per second) justifies it.
    private readonly Dictionary<int, (string? Sid, long Timestamp)> _pidSidCache = new();
    private readonly Lock _lock = new();
    private Timer? _timer;
    private volatile bool _disposed;

    public void Start()
    {
        if (!startTimer)
        {
            OnTimerTick(null);
            return;
        }

        var timer = new Timer(OnTimerTick, null, TimeSpan.Zero, TimeSpan.FromSeconds(1));
        _timer = timer;
        if (_disposed) { _timer = null; timer.Dispose(); }
    }

    private void OnTimerTick(object? state)
    {
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            ScanAndApply();
            if (sw.ElapsedMilliseconds > 100)
                log.Warn($"WfpEphemeralPortScanner: scan took {sw.ElapsedMilliseconds} ms");
        }
        catch (ObjectDisposedException) { }
        catch (Exception ex) { log.Warn($"WfpEphemeralPortScanner: scan failed: {ex.Message}"); }
    }

    private void ScanAndApply()
    {
        var snapshot = db.CreateSnapshot();

        var blockedAccounts = snapshot.Accounts
            .Where(a => !a.Firewall.IsDefault && !a.Firewall.AllowLocalhost)
            .Select(a => new BlockedAccount(
                Sid: a.Sid,
                ExemptedRanges: a.Firewall.LocalhostPortExemptions
                    .Select(p => LocalhostPortParser.ParsePortOrRange(p))
                    .Where(r => r.HasValue).Select(r => r!.Value).ToList(),
                FilterEphemeral: a.Firewall.FilterEphemeralLoopback))
            .ToList();

        if (blockedAccounts.Count == 0)
            return;

        var eligibleAccounts = blockedAccounts.Where(a => a.FilterEphemeral).ToList();
        var clearAccounts = blockedAccounts.Where(a => !a.FilterEphemeral).ToList();

        Dictionary<int, string?> portToSid = [];
        if (eligibleAccounts.Count > 0)
            portToSid = CollectListeningEphemeralPorts();

        foreach (var account in eligibleAccounts)
        {
            var ranges = ComputeBlockedEphemeralRanges(account.Sid, account.ExemptedRanges, portToSid);
            wfpBlocker.UpdateEphemeralPorts(account.Sid, ranges);
        }

        foreach (var account in clearAccounts)
            wfpBlocker.UpdateEphemeralPorts(account.Sid, []);
    }

    private Dictionary<int, string?> CollectListeningEphemeralPorts()
    {
        var result = new Dictionary<int, string?>();

        var allPorts = new List<(int Port, int Pid)>();
        allPorts.AddRange(ReadListeningPorts(isTcp: true, isIPv6: false));
        allPorts.AddRange(ReadListeningPorts(isTcp: true, isIPv6: true));
        allPorts.AddRange(ReadListeningPorts(isTcp: false, isIPv6: false));
        allPorts.AddRange(ReadListeningPorts(isTcp: false, isIPv6: true));

        var observedPids = new HashSet<int>();

        foreach (var (port, pid) in allPorts)
        {
            if (port < EphemeralPortRangeStart)
                continue;

            observedPids.Add(pid);

            string? ownerSid;
            var now = Environment.TickCount64;
            bool fresh;
            lock (_lock)
            {
                fresh = _pidSidCache.TryGetValue(pid, out var cached)
                    && now - cached.Timestamp < 10_000;
                if (fresh)
                {
                    ownerSid = cached.Sid;
                    _pidSidCache[pid] = (cached.Sid, now);
                }
                else
                {
                    ownerSid = null;
                }
            }

            if (!fresh)
            {
                ownerSid = NativeTokenHelper.TryGetProcessOwnerSid((uint)pid)?.Value;
                lock (_lock) { _pidSidCache[pid] = (ownerSid, now); }
            }

            // Null-wins: if any endpoint on this port has an unknown owner, treat as cross-user.
            if (!result.TryGetValue(port, out var existing) || existing != null)
                result[port] = ownerSid;
        }

        // Prune PIDs no longer observed in the current scan
        lock (_lock)
        {
            var stale = _pidSidCache.Keys.Where(pid => !observedPids.Contains(pid)).ToList();
            foreach (var pid in stale)
                _pidSidCache.Remove(pid);
        }

        return result;
    }

    private List<(int Port, int Pid)> ReadListeningPorts(bool isTcp, bool isIPv6)
    {
        int af = isIPv6 ? IphlpapiNative.AF_INET6 : IphlpapiNative.AF_INET;
        int size = 0;
        IntPtr buf = IntPtr.Zero;

        try
        {
            int rc;
            if (isTcp)
                rc = IphlpapiNative.GetExtendedTcpTable(IntPtr.Zero, ref size, false, af,
                    IphlpapiNative.TcpTableClass.OwnerPidListener, 0);
            else
                rc = IphlpapiNative.GetExtendedUdpTable(IntPtr.Zero, ref size, false, af,
                    IphlpapiNative.UdpTableClass.OwnerPid, 0);

            if (rc != IphlpapiNative.ERROR_INSUFFICIENT_BUFFER)
            {
                // ERROR_INVALID_PARAMETER when IPv6 is disabled — expected, not an error.
                if (rc != IphlpapiNative.ERROR_INVALID_PARAMETER)
                    log.Warn($"WfpEphemeralPortScanner: {(isTcp ? "TCP" : "UDP")}/{(isIPv6 ? "IPv6" : "IPv4")} table query failed (0x{rc:X8})");
                return [];
            }

            buf = Marshal.AllocHGlobal(size);

            if (isTcp)
                rc = IphlpapiNative.GetExtendedTcpTable(buf, ref size, false, af,
                    IphlpapiNative.TcpTableClass.OwnerPidListener, 0);
            else
                rc = IphlpapiNative.GetExtendedUdpTable(buf, ref size, false, af,
                    IphlpapiNative.UdpTableClass.OwnerPid, 0);

            if (rc == IphlpapiNative.ERROR_INSUFFICIENT_BUFFER)
            {
                // Table grew between calls — retry once with larger buffer
                Marshal.FreeHGlobal(buf);
                buf = Marshal.AllocHGlobal(size);

                if (isTcp)
                    rc = IphlpapiNative.GetExtendedTcpTable(buf, ref size, false, af,
                        IphlpapiNative.TcpTableClass.OwnerPidListener, 0);
                else
                    rc = IphlpapiNative.GetExtendedUdpTable(buf, ref size, false, af,
                        IphlpapiNative.UdpTableClass.OwnerPid, 0);
            }

            if (rc != IphlpapiNative.ERROR_SUCCESS)
            {
                log.Warn($"WfpEphemeralPortScanner: {(isTcp ? "TCP" : "UDP")}/{(isIPv6 ? "IPv6" : "IPv4")} table query failed (0x{rc:X8})");
                return [];
            }

            return ParsePortRows(buf, isTcp, isIPv6);
        }
        finally
        {
            if (buf != IntPtr.Zero)
                Marshal.FreeHGlobal(buf);
        }
    }

    private static List<(int Port, int Pid)> ParsePortRows(IntPtr buf, bool isTcp, bool isIPv6)
    {
        // Row layouts (parsed manually from unmanaged buffers):
        // IPv4 TCP (MIB_TCPROW_OWNER_PID):  stride=24, portOff=8,  pidOff=20
        // IPv4 UDP (MIB_UDPROW_OWNER_PID):  stride=12, portOff=4,  pidOff=8
        // IPv6 TCP (MIB_TCP6ROW_OWNER_PID): stride=56, portOff=20, pidOff=52
        // IPv6 UDP (MIB_UDP6ROW_OWNER_PID): stride=28, portOff=20, pidOff=24
        int stride, portOff, pidOff;
        if (isTcp && !isIPv6)      { stride = 24; portOff = 8;  pidOff = 20; }
        else if (!isTcp && !isIPv6){ stride = 12; portOff = 4;  pidOff = 8;  }
        else if (isTcp)            { stride = 56; portOff = 20; pidOff = 52; }
        else                       { stride = 28; portOff = 20; pidOff = 24; }

        var numEntries = Marshal.ReadInt32(buf, 0);
        var result = new List<(int, int)>(numEntries);

        for (var i = 0; i < numEntries; i++)
        {
            var rowOffset = 4 + i * stride;
            int raw = Marshal.ReadInt32(buf, rowOffset + portOff) & 0xFFFF;
            int port = ((raw >> 8) & 0xFF) | ((raw & 0xFF) << 8);
            int pid = Marshal.ReadInt32(buf, rowOffset + pidOff);
            result.Add((port, pid));
        }

        return result;
    }

    /// <summary>
    /// Computes the ephemeral port ranges to block for the given account. A cross-user port is
    /// included if it falls within at least one range exemption (r.Low &lt; r.High) AND does not
    /// appear in any single-port exemption (r.Low == r.High).
    /// </summary>
    public static List<PortRange> ComputeBlockedEphemeralRanges(
        string accountSid, IReadOnlyList<PortRange> exemptedRanges, Dictionary<int, string?> portToSid)
    {
        var blocked = new List<int>();

        foreach (var (port, ownerSid) in portToSid)
        {
            // Same-user: never block
            if (ownerSid != null && string.Equals(ownerSid, accountSid, StringComparison.OrdinalIgnoreCase))
                continue;

            // Not in any range exemption: static filter already blocks it — scanner must not re-block
            if (!exemptedRanges.Any(r => r.Low < r.High && r.Low <= port && port <= r.High))
                continue;

            // Single-port exemption: explicit user-allowed port; scanner never overrides it
            if (exemptedRanges.Any(r => r.Low == r.High && r.Low == port))
                continue;

            blocked.Add(port);
        }

        return LocalhostPortParser.CoalescePortRanges(blocked);
    }

    public void Dispose()
    {
        _disposed = true;
        _timer?.Dispose();
        _timer = null;
    }
}
