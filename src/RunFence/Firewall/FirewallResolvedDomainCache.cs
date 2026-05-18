using RunFence.Core.Models;

namespace RunFence.Firewall;

public class FirewallResolvedDomainCache(FirewallDomainDirtyTracker dirtyTracker)
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, List<string>> _resolvedDomains = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, IReadOnlyList<string>> GetAccountSnapshot(FirewallAccountSettings settings)
    {
        lock (_lock)
        {
            return SnapshotRequestedDomains(FirewallDomainDirtyTracker.GetAllowlistDomains(settings));
        }
    }

    public IReadOnlyDictionary<string, IReadOnlyList<string>> GetGlobalSnapshot()
    {
        lock (_lock)
        {
            return SnapshotDomainMap(_resolvedDomains);
        }
    }

    public void MarkDirty(string sid, IEnumerable<string> domains) =>
        dirtyTracker.MarkDirty(sid, domains);

    public IReadOnlyCollection<string> UpdateResolvedDomainsAndGetChangedDomains(
        IReadOnlyCollection<string> requestedDomains,
        Dictionary<string, List<string>> freshResolved)
    {
        lock (_lock)
        {
            var changedDomains = new List<string>();
            foreach (var domain in requestedDomains.DistinctCaseInsensitive())
            {
                if (!freshResolved.TryGetValue(domain, out var freshAddresses) || freshAddresses.Count == 0)
                    continue;

                var normalizedFreshAddresses = freshAddresses.DistinctCaseInsensitive().ToList();
                if (normalizedFreshAddresses.Count == 0)
                    continue;

                // Keep last-good resolved IPs when a domain cannot be resolved right now:
                // this prevents an allowlist from silently loosening during transient DNS failures.
                if (!_resolvedDomains.TryGetValue(domain, out var cachedAddresses)
                    || cachedAddresses.Count == 0
                    || !AddressSetsEqual(cachedAddresses, normalizedFreshAddresses))
                    changedDomains.Add(domain);

                _resolvedDomains[domain] = normalizedFreshAddresses;
            }

            return changedDomains;
        }
    }

    public void MarkDirtyForChangedDomains(AppDatabase database, IReadOnlyCollection<string> changedDomains) =>
        dirtyTracker.MarkDirtyForChangedDomains(database, changedDomains);

    public DomainRefreshDecision GetRefreshDecision(
        string sid,
        FirewallAccountSettings settings,
        IReadOnlySet<string> changedDomains)
    {
        var requestedDomains = FirewallDomainDirtyTracker.GetAllowlistDomains(settings).ToList();
        bool dnsChanged = requestedDomains.Any(changedDomains.Contains);
        bool wasDirty = dirtyTracker.IsAnyDirty(sid, requestedDomains);

        IReadOnlyDictionary<string, IReadOnlyList<string>> resolvedDomains;
        lock (_lock)
        {
            resolvedDomains = SnapshotRequestedDomains(requestedDomains);
        }

        var domainsToClearOnSuccess = requestedDomains
            .Where(domain => resolvedDomains.TryGetValue(domain, out var v) && v.Count > 0)
            .ToList();

        return new DomainRefreshDecision(
            ShouldRefreshRules: dnsChanged || wasDirty,
            WasDirty: wasDirty,
            ResolvedDomains: resolvedDomains,
            DomainsToClearOnSuccess: domainsToClearOnSuccess);
    }

    public void MarkRefreshSucceeded(string sid, IReadOnlyCollection<string> domains) =>
        dirtyTracker.MarkRefreshSucceeded(sid, domains);

    public void Prune(AppDatabase database)
    {
        var activeDomainsBySid = BuildActiveDomainMap(database);
        var activeGlobalDomains = activeDomainsBySid.Values
            .SelectMany(domains => domains)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        lock (_lock)
        {
            foreach (var domain in _resolvedDomains.Keys.ToList())
            {
                if (!activeGlobalDomains.Contains(domain))
                    _resolvedDomains.Remove(domain);
            }
        }

        dirtyTracker.PruneDirtyDomains(activeDomainsBySid);
    }

    public void Clear()
    {
        lock (_lock)
        {
            _resolvedDomains.Clear();
        }

        dirtyTracker.Clear();
    }

    private IReadOnlyDictionary<string, IReadOnlyList<string>> SnapshotRequestedDomains(IEnumerable<string> requestedDomains)
    {
        var snapshot = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var domain in requestedDomains)
        {
            if (_resolvedDomains.TryGetValue(domain, out var addresses) && addresses.Count > 0)
                snapshot.TryAdd(domain, addresses.ToList());
        }

        return snapshot;
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> SnapshotDomainMap(
        IReadOnlyDictionary<string, List<string>> source)
    {
        var snapshot = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (domain, addresses) in source)
            snapshot[domain] = addresses.ToList();
        return snapshot;
    }

    private static bool AddressSetsEqual(IReadOnlyCollection<string> first, IReadOnlyCollection<string> second)
        => new HashSet<string>(first, StringComparer.OrdinalIgnoreCase)
            .SetEquals(second);

    private static IReadOnlyDictionary<string, HashSet<string>> BuildActiveDomainMap(AppDatabase database)
    {
        var activeDomainsBySid = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var account in database.Accounts)
            activeDomainsBySid[account.Sid] = FirewallDomainDirtyTracker.GetAllowlistDomains(account.Firewall).ToHashSet(StringComparer.OrdinalIgnoreCase);

        return activeDomainsBySid;
    }
}
