using RunFence.Core.Models;

namespace RunFence.Firewall;

public class FirewallEnforcementRetryState
{
    private readonly Lock _lock = new();
    private readonly HashSet<string> _dnsServerRefreshPendingSids = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<string> _lastDnsServers = [];
    private bool _globalIcmpDirty;

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
        lock (_lock)
        {
            _globalIcmpDirty = true;
        }
    }

    public void MarkGlobalIcmpSucceeded()
    {
        lock (_lock)
        {
            _globalIcmpDirty = false;
        }
    }

    public bool IsGlobalIcmpDirty()
    {
        lock (_lock)
        {
            return _globalIcmpDirty;
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
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _dnsServerRefreshPendingSids.Clear();
            _lastDnsServers = [];
            _globalIcmpDirty = false;
        }
    }

    private static bool DnsServersEqual(IReadOnlyList<string> first, IReadOnlyList<string> second)
        => new HashSet<string>(first, StringComparer.OrdinalIgnoreCase)
            .SetEquals(second);
}
