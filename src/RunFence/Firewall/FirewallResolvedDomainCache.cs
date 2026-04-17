using RunFence.Core.Models;

namespace RunFence.Firewall;

public class FirewallResolvedDomainCache
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, List<string>> _resolvedDomains = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<string>> _dirtyDomainsBySid = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, IReadOnlyList<string>> GetAccountSnapshot(FirewallAccountSettings settings)
    {
        lock (_lock)
        {
            return SnapshotRequestedDomains(GetAllowlistDomains(settings));
        }
    }

    public IReadOnlyDictionary<string, IReadOnlyList<string>> GetGlobalSnapshot()
    {
        lock (_lock)
        {
            return SnapshotDomainMap(_resolvedDomains);
        }
    }

    public void MarkDirty(string sid, IEnumerable<string> domains)
    {
        lock (_lock)
        {
            var dirtyDomains = GetOrCreateDirtyDomains(sid);
            foreach (var domain in domains.DistinctCaseInsensitive())
                dirtyDomains.Add(domain);
            RemoveEmptyDirtySet(sid);
        }
    }

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

                if (!_resolvedDomains.TryGetValue(domain, out var cachedAddresses)
                    || cachedAddresses.Count == 0
                    || !AddressSetsEqual(cachedAddresses, normalizedFreshAddresses))
                    changedDomains.Add(domain);

                _resolvedDomains[domain] = normalizedFreshAddresses;
            }

            return changedDomains;
        }
    }

    public void MarkDirtyForChangedDomains(AppDatabase database, IReadOnlyCollection<string> changedDomains)
    {
        lock (_lock)
        {
            var changedDomainSet = changedDomains.DistinctCaseInsensitive().ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (changedDomainSet.Count == 0)
                return;

            foreach (var account in database.Accounts)
            {
                var settings = account.Firewall;
                if (!IsEligibleForDomainAllowlistRefresh(settings))
                    continue;

                var dirtyDomains = GetOrCreateDirtyDomains(account.Sid);
                foreach (var domain in GetAllowlistDomains(settings))
                {
                    if (changedDomainSet.Contains(domain))
                        dirtyDomains.Add(domain);
                }

                RemoveEmptyDirtySet(account.Sid);
            }
        }
    }

    public DomainRefreshDecision GetRefreshDecision(
        string sid,
        FirewallAccountSettings settings,
        IReadOnlySet<string> changedDomains)
    {
        lock (_lock)
        {
            var requestedDomains = GetAllowlistDomains(settings).ToList();
            bool dnsChanged = requestedDomains.Any(changedDomains.Contains);
            bool wasDirty = IsAnyDirty(sid, requestedDomains);
            var resolvedDomains = SnapshotRequestedDomains(requestedDomains);
            var domainsToClearOnSuccess = requestedDomains
                .Where(domain => resolvedDomains.TryGetValue(domain, out var v) && v.Count > 0)
                .ToList();

            return new DomainRefreshDecision(
                ShouldRefreshRules: dnsChanged || wasDirty,
                DnsChanged: dnsChanged,
                WasDirty: wasDirty,
                ResolvedDomains: resolvedDomains,
                DomainsToClearOnSuccess: domainsToClearOnSuccess);
        }
    }

    public void MarkRefreshSucceeded(string sid, IReadOnlyCollection<string> domains)
    {
        lock (_lock)
        {
            if (!_dirtyDomainsBySid.TryGetValue(sid, out var dirtyDomains))
                return;

            foreach (var domain in domains.DistinctCaseInsensitive())
                dirtyDomains.Remove(domain);

            RemoveEmptyDirtySet(sid);
        }
    }

    public void Prune(AppDatabase database)
    {
        lock (_lock)
        {
            var activeDomainsBySid = BuildActiveDomainMap(database);
            var activeGlobalDomains = activeDomainsBySid.Values
                .SelectMany(domains => domains)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var domain in _resolvedDomains.Keys.ToList())
            {
                if (!activeGlobalDomains.Contains(domain))
                    _resolvedDomains.Remove(domain);
            }

            PruneDirtyDomains(activeDomainsBySid);
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _resolvedDomains.Clear();
            _dirtyDomainsBySid.Clear();
        }
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

    private HashSet<string> GetOrCreateDirtyDomains(string sid)
    {
        if (!_dirtyDomainsBySid.TryGetValue(sid, out var dirtyDomains))
        {
            dirtyDomains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _dirtyDomainsBySid[sid] = dirtyDomains;
        }

        return dirtyDomains;
    }

    private bool IsAnyDirty(string sid, IEnumerable<string> domains)
    {
        return _dirtyDomainsBySid.TryGetValue(sid, out var dirtyDomains)
            && domains.Any(dirtyDomains.Contains);
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
            activeDomainsBySid[account.Sid] = GetAllowlistDomains(account.Firewall).ToHashSet(StringComparer.OrdinalIgnoreCase);

        return activeDomainsBySid;
    }

    private void PruneDirtyDomains(IReadOnlyDictionary<string, HashSet<string>> activeDomainsBySid)
    {
        foreach (var sid in _dirtyDomainsBySid.Keys.ToList())
        {
            if (!activeDomainsBySid.TryGetValue(sid, out var activeDomains))
            {
                _dirtyDomainsBySid.Remove(sid);
                continue;
            }

            var dirtyDomains = _dirtyDomainsBySid[sid];
            dirtyDomains.RemoveWhere(domain => !activeDomains.Contains(domain));
            RemoveEmptyDirtySet(sid);
        }
    }

    private void RemoveEmptyDirtySet(string sid)
    {
        if (_dirtyDomainsBySid.TryGetValue(sid, out var dirtyDomains) && dirtyDomains.Count == 0)
            _dirtyDomainsBySid.Remove(sid);
    }

    private static bool IsEligibleForDomainAllowlistRefresh(FirewallAccountSettings settings)
        => !settings.IsDefault
            && settings is not { AllowInternet: true, AllowLan: true }
            && GetAllowlistDomains(settings).Any();

    private static IEnumerable<string> GetAllowlistDomains(FirewallAccountSettings settings)
        => settings.Allowlist
            .Where(entry => entry.IsDomain)
            .Select(entry => entry.Value)
            .DistinctCaseInsensitive();
}
