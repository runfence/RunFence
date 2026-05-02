using RunFence.Core.Models;

namespace RunFence.Firewall;

/// <summary>
/// Tracks which firewall domain allowlist entries are dirty (have pending resolution changes
/// that have not yet been applied to OS rules). Thread-safe via an internal lock.
/// </summary>
public class FirewallDomainDirtyTracker
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, HashSet<string>> _dirtyDomainsBySid = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Marks the specified domains as dirty for the given SID.
    /// </summary>
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

    /// <summary>
    /// Marks domains dirty for any account in <paramref name="database"/> whose allowlist intersects
    /// with <paramref name="changedDomains"/>.
    /// </summary>
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

    /// <summary>
    /// Returns true if any of the specified domains are marked dirty for the given SID.
    /// </summary>
    public bool IsAnyDirty(string sid, IEnumerable<string> domains)
    {
        lock (_lock)
        {
            return _dirtyDomainsBySid.TryGetValue(sid, out var dirtyDomains)
                && domains.Any(dirtyDomains.Contains);
        }
    }

    /// <summary>
    /// Clears the dirty flag for the specified domains under the given SID (called after a successful refresh).
    /// </summary>
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

    /// <summary>
    /// Removes dirty entries for SIDs and domains no longer active in <paramref name="activeDomainsBySid"/>.
    /// </summary>
    public void PruneDirtyDomains(IReadOnlyDictionary<string, HashSet<string>> activeDomainsBySid)
    {
        lock (_lock)
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
    }

    public void Clear()
    {
        lock (_lock)
        {
            _dirtyDomainsBySid.Clear();
        }
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

    private void RemoveEmptyDirtySet(string sid)
    {
        if (_dirtyDomainsBySid.TryGetValue(sid, out var dirtyDomains) && dirtyDomains.Count == 0)
            _dirtyDomainsBySid.Remove(sid);
    }

    private static bool IsEligibleForDomainAllowlistRefresh(FirewallAccountSettings settings)
        => !settings.IsDefault
            && settings is not { AllowInternet: true, AllowLan: true }
            && GetAllowlistDomains(settings).Any();

    public static IEnumerable<string> GetAllowlistDomains(FirewallAccountSettings settings)
        => settings.Allowlist
            .Where(entry => entry.IsDomain)
            .Select(entry => entry.Value)
            .DistinctCaseInsensitive();
}
