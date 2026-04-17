using RunFence.Core;
using RunFence.Firewall;

namespace RunFence.Firewall.Wfp;

/// <summary>
/// Manages persistent WFP filters for per-user loopback blocking.
/// Windows Firewall (INetFwRule) implicitly excludes loopback traffic, so direct WFP filters at
/// FWPM_LAYER_ALE_AUTH_CONNECT_V4/V6 are required to correctly block loopback for a specified user SID.
/// Filter keys are derived deterministically from the SID so filters can be found and deleted without
/// storing IDs in the database.
///
/// Per account, up to 2 + 2*N BLOCK filters are installed when blocking is active:
///   RunFence-LH-V4/V6       — persistent static complement filters blocking ports 1–65535 minus exemption list
///   RunFence-LH-EV4-{n}/EV6-{n} — non-persistent ephemeral filters blocking dynamically-discovered cross-user ports
/// Static and ephemeral filters are managed independently: <see cref="Apply"/> writes V4/V6 (persistent),
/// <see cref="UpdateEphemeralPorts"/> writes EV4/EV6 (non-persistent, session-scoped).
///
/// Ephemeral filters use a long-lived WFP engine handle (<see cref="_ephemeralHandle"/>) and are
/// non-persistent: they are automatically removed when the handle is closed (process exit/dispose).
/// This avoids BFE persistent store churn from rapid 1-second filter updates, which causes
/// AccessViolationException in FwpmFilterAdd0.
///
/// Ephemeral ranges are chunked across multiple filters when the range count exceeds
/// <see cref="MaxPortRangesPerFilter"/> to stay within WFP per-filter condition limits.
/// </summary>
public sealed class WfpLocalhostBlocker(ILoggingService log, IWfpFilterHelper filterHelper) : IWfpLocalhostBlocker, IDisposable
{
    private readonly WfpLocalhostFilterWriter _filterWriter = new(filterHelper);
    private readonly WfpTransactionHelper _txHelper = new(log);

    /// <summary>
    /// Maximum port range conditions per WFP filter. Each filter has 2 base conditions
    /// (loopback flag + user SID) plus N port ranges → 2+N total. WFP limits conditions
    /// per filter; 45 keeps us at 47 total, well under the limit.
    /// </summary>
    internal const int MaxPortRangesPerFilter = 45;

    /// <summary>SIDs with active localhost blocking → their exempted port ranges.</summary>
    private readonly Dictionary<string, IReadOnlyList<PortRange>> _staticState =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>SIDs → currently installed ephemeral blocked ranges + filter count (for cleanup).</summary>
    private readonly Dictionary<string, EphemeralEntry> _ephemeralState =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Long-lived WFP engine handle for non-persistent ephemeral filters.
    /// Non-persistent filters are automatically removed when the handle is closed.
    /// </summary>
    private IntPtr _ephemeralHandle;

    private readonly Lock _lock = new();

    private record EphemeralEntry(IReadOnlyList<PortRange> Ranges, int FilterCount);

    public void Apply(string sid, bool block, IReadOnlyList<string> allowedPorts)
    {
        lock (_lock)
        {
            var exemptedRanges = allowedPorts
                .Select(p => LocalhostPortParser.ParsePortOrRange(p))
                .Where(r => r.HasValue)
                .Select(r => r!.Value)
                .ToList();

            if (block)
            {
                var blockedRanges = WfpLocalhostFilterWriter.BuildBlockedPortRanges(exemptedRanges);

                log.Info($"WfpLocalhostBlocker: blocking loopback for {sid}, exempted: [{string.Join(", ", exemptedRanges)}], blocking ports 1-65535");

                bool committed = _txHelper.ExecuteInTransaction("WfpLocalhostBlocker", handle =>
                {
                    var v4Key = WfpFilterKeyHelper.DeriveKey("RunFence-LH-V4", sid);
                    var v6Key = WfpFilterKeyHelper.DeriveKey("RunFence-LH-V6", sid);
                    var sddl = FirewallSddlHelper.BuildSddl(sid);
                    _filterWriter.DeleteFilter(handle, ref v4Key);
                    _filterWriter.DeleteFilter(handle, ref v6Key);
                    _filterWriter.AddLocalhostFilter(handle, v4Key, sddl, isIPv6: false, blockedRanges);
                    _filterWriter.AddLocalhostFilter(handle, v6Key, sddl, isIPv6: true, blockedRanges);
                });

                if (committed)
                    _staticState[sid] = exemptedRanges;
            }
            else
            {
                var ephemeralFilterCount = _ephemeralState.GetValueOrDefault(sid)?.FilterCount ?? 0;
                log.Info($"WfpLocalhostBlocker: removing loopback block for {sid}");

                bool committed = _txHelper.ExecuteInTransaction("WfpLocalhostBlocker", handle =>
                {
                    var v4Key = WfpFilterKeyHelper.DeriveKey("RunFence-LH-V4", sid);
                    var v6Key = WfpFilterKeyHelper.DeriveKey("RunFence-LH-V6", sid);
                    _filterWriter.DeleteFilter(handle, ref v4Key);
                    _filterWriter.DeleteFilter(handle, ref v6Key);
                });

                if (committed)
                {
                    _staticState.Remove(sid);
                    _ephemeralState.Remove(sid);
                }

                // Delete non-persistent ephemeral filters on their own handle. This runs even when
                // the persistent filter removal transaction above failed (committed == false) — best-effort
                // cleanup for a rare edge case. Ephemeral filters are non-persistent and automatically
                // removed on process exit, so any failure here is self-correcting.
                if (ephemeralFilterCount > 0)
                {
                    var handle = EnsureEphemeralHandle();
                    if (handle != IntPtr.Zero)
                    {
                        _txHelper.ExecuteOnHandle("WfpLocalhostBlocker", handle, h =>
                            DeleteEphemeralFilters(h, sid, ephemeralFilterCount));
                    }
                }
            }
        }
    }

    public void UpdateEphemeralPorts(string sid, IReadOnlyList<PortRange> ephemeralBlockedRanges)
    {
        lock (_lock)
        {
            if (!_staticState.ContainsKey(sid))
                return;

            _ephemeralState.TryGetValue(sid, out var existing);
            if (existing != null && existing.Ranges.SequenceEqual(ephemeralBlockedRanges))
                return;

            var handle = EnsureEphemeralHandle();
            if (handle == IntPtr.Zero)
                return;

            var oldFilterCount = existing?.FilterCount ?? 0;
            var newFilterCount = (ephemeralBlockedRanges.Count + MaxPortRangesPerFilter - 1) / MaxPortRangesPerFilter;

            log.Info($"WfpLocalhostBlocker: updating ephemeral blocks for {sid}: [{string.Join(", ", ephemeralBlockedRanges)}]");

            bool committed = _txHelper.ExecuteOnHandle("WfpLocalhostBlocker", handle, h =>
            {
                var sddl = FirewallSddlHelper.BuildSddl(sid);
                DeleteEphemeralFilters(h, sid, oldFilterCount);

                for (var chunk = 0; chunk < newFilterCount; chunk++)
                {
                    var offset = chunk * MaxPortRangesPerFilter;
                    var count = Math.Min(MaxPortRangesPerFilter, ephemeralBlockedRanges.Count - offset);
                    var chunkRanges = ephemeralBlockedRanges.Skip(offset).Take(count).ToList();

                    var ev4Key = WfpFilterKeyHelper.DeriveKey($"RunFence-LH-EV4-{chunk}", sid);
                    var ev6Key = WfpFilterKeyHelper.DeriveKey($"RunFence-LH-EV6-{chunk}", sid);
                    _filterWriter.AddLocalhostFilter(h, ev4Key, sddl, isIPv6: false, chunkRanges, persistent: false);
                    _filterWriter.AddLocalhostFilter(h, ev6Key, sddl, isIPv6: true, chunkRanges, persistent: false);
                }
            });

            if (committed)
                _ephemeralState[sid] = new EphemeralEntry(ephemeralBlockedRanges, newFilterCount);
        }
    }

    private IntPtr EnsureEphemeralHandle()
    {
        if (_ephemeralHandle != IntPtr.Zero)
            return _ephemeralHandle;

        var rc = WfpNative.FwpmEngineOpen0(null, WfpNative.RPC_C_AUTHN_DEFAULT,
            IntPtr.Zero, IntPtr.Zero, out _ephemeralHandle);
        if (rc != WfpNative.ERROR_SUCCESS)
        {
            _ephemeralHandle = IntPtr.Zero;
            log.Warn($"WfpLocalhostBlocker: FwpmEngineOpen0 failed for ephemeral session (0x{rc:X8})");
        }
        return _ephemeralHandle;
    }

    private void DeleteEphemeralFilters(IntPtr handle, string sid, int filterCount)
    {
        for (var chunk = 0; chunk < filterCount; chunk++)
        {
            var ev4Key = WfpFilterKeyHelper.DeriveKey($"RunFence-LH-EV4-{chunk}", sid);
            var ev6Key = WfpFilterKeyHelper.DeriveKey($"RunFence-LH-EV6-{chunk}", sid);
            _filterWriter.DeleteFilter(handle, ref ev4Key);
            _filterWriter.DeleteFilter(handle, ref ev6Key);
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_ephemeralHandle != IntPtr.Zero)
            {
                WfpNative.FwpmEngineClose0(_ephemeralHandle);
                _ephemeralHandle = IntPtr.Zero;
            }
        }
    }
}
