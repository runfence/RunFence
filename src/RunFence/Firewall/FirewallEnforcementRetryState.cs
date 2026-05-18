using RunFence.Core.Models;

namespace RunFence.Firewall;

public class FirewallEnforcementRetryState
{
    private const string GlobalScopeKey = "__global__";
    private readonly Lock _lock = new();
    private readonly HashSet<string> _dnsServerRefreshPendingSids = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(FirewallEnforcementLayer Layer, string Key), RetryEntry> _retryEntries = new();
    private IReadOnlyList<string> _lastDnsServers = [];

    public sealed record RetryEntry(
        FirewallEnforcementLayer Layer,
        string Key,
        string? LastError,
        DateTimeOffset LastAttemptUtc,
        string RetryReason);

    public bool UpdateDnsServersAndReturnChanged(IReadOnlyList<string> dnsServers)
    {
        lock (_lock)
        {
            if (DnsServersEqual(_lastDnsServers, dnsServers))
                return false;

            _lastDnsServers = dnsServers.ToList();
            return true;
        }
    }

    public void MarkDnsServerRefreshPending(IEnumerable<string> sids)
    {
        lock (_lock)
        {
            foreach (var sid in sids)
            {
                if (!string.IsNullOrWhiteSpace(sid))
                    _dnsServerRefreshPendingSids.Add(sid);
            }
        }
    }

    public void MarkDnsServerRefreshSucceeded(string sid)
    {
        lock (_lock)
        {
            _dnsServerRefreshPendingSids.Remove(sid);
        }
    }

    public IReadOnlySet<string> GetDnsServerRefreshPendingSids()
    {
        lock (_lock)
        {
            return new HashSet<string>(_dnsServerRefreshPendingSids, StringComparer.OrdinalIgnoreCase);
        }
    }

    public void MarkGlobalIcmpDirty()
    {
        MarkRetryPending(FirewallEnforcementLayer.GlobalIcmp, GlobalScopeKey, null, "Global ICMP enforcement failed.");
    }

    public void MarkGlobalIcmpSucceeded()
    {
        MarkRetrySucceeded(FirewallEnforcementLayer.GlobalIcmp, GlobalScopeKey);
    }

    public bool IsGlobalIcmpDirty()
    {
        lock (_lock)
        {
            return _retryEntries.ContainsKey((FirewallEnforcementLayer.GlobalIcmp, GlobalScopeKey));
        }
    }

    public void MarkRetryPending(FirewallEnforcementLayer layer, string key, string? error, string retryReason)
    {
        lock (_lock)
        {
            _retryEntries[(layer, key)] = new RetryEntry(
                layer,
                key,
                error,
                DateTimeOffset.UtcNow,
                retryReason);
        }
    }

    public void MarkRetrySucceeded(FirewallEnforcementLayer layer, string key)
    {
        lock (_lock)
        {
            _retryEntries.Remove((layer, key));
        }
    }

    public IReadOnlyList<RetryEntry> GetRetryEntries()
    {
        lock (_lock)
        {
            return _retryEntries.Values.ToList();
        }
    }

    public void Prune(AppDatabase database)
    {
        lock (_lock)
        {
            var activeSids = database.Accounts
                .Select(account => account.Sid)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            _dnsServerRefreshPendingSids.RemoveWhere(sid => !activeSids.Contains(sid));
            var sidScopedLayers = new HashSet<FirewallEnforcementLayer>
            {
                FirewallEnforcementLayer.AccountRules,
                FirewallEnforcementLayer.WfpFilters,
                FirewallEnforcementLayer.DnsRefresh
            };
            var staleKeys = _retryEntries.Keys
                .Where(entry => sidScopedLayers.Contains(entry.Layer) && !activeSids.Contains(entry.Key))
                .ToList();
            foreach (var staleKey in staleKeys)
                _retryEntries.Remove(staleKey);
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _dnsServerRefreshPendingSids.Clear();
            _lastDnsServers = [];
            _retryEntries.Clear();
        }
    }

    private static bool DnsServersEqual(IReadOnlyList<string> first, IReadOnlyList<string> second)
        => new HashSet<string>(first, StringComparer.OrdinalIgnoreCase)
            .SetEquals(second);
}
