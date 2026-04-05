using RunFence.Core.Models;

namespace RunFence.Persistence;

/// <summary>
/// Tracks which additional config file each grant (SID + path + isDeny + isTraverseOnly) belongs to.
/// Entries absent from the map belong to the main config.
/// </summary>
public class GrantConfigTracker : IGrantConfigTracker
{
    private readonly Dictionary<GrantKey, string> _grantConfigMap = new();

    public void AssignGrant(string sid, GrantedPathEntry entry, string? configPath)
    {
        var key = new GrantKey(sid, entry);
        if (configPath == null)
            _grantConfigMap.Remove(key);
        else
            _grantConfigMap[key] = Path.GetFullPath(configPath);
    }

    public void RemoveGrant(string sid, GrantedPathEntry entry)
    {
        _grantConfigMap.Remove(new GrantKey(sid, entry));
    }

    public string? GetGrantConfigPath(string sid, GrantedPathEntry entry)
    {
        return _grantConfigMap.GetValueOrDefault(new GrantKey(sid, entry));
    }

    public bool IsInMainConfig(string sid, GrantedPathEntry entry)
    {
        return !_grantConfigMap.ContainsKey(new GrantKey(sid, entry));
    }

    public List<AppConfigAccountEntry>? FilterGrantsForConfig(List<AccountEntry> accounts, string configPath)
    {
        var result = new List<AppConfigAccountEntry>();
        foreach (var account in accounts)
        {
            var filtered = account.Grants.Where(e =>
            {
                _grantConfigMap.TryGetValue(new GrantKey(account.Sid, e), out var trackedPath);
                return trackedPath != null &&
                       string.Equals(trackedPath, configPath, StringComparison.OrdinalIgnoreCase);
            }).ToList();
            if (filtered.Count > 0)
                result.Add(new AppConfigAccountEntry { Sid = account.Sid, Grants = filtered });
        }

        return result.Count > 0 ? result : null;
    }

    /// <summary>
    /// Removes all grant entries that belong to <paramref name="configPath"/> from the tracker.
    /// Returns the keys that were removed so callers can update the database.
    /// </summary>
    public List<(string Sid, string Path, bool IsDeny, bool IsTraverseOnly)> UnregisterConfigGrants(string configPath)
    {
        var normalized = Path.GetFullPath(configPath);
        var keysToRemove = _grantConfigMap
            .Where(kvp => string.Equals(kvp.Value, normalized, StringComparison.OrdinalIgnoreCase))
            .Select(kvp => kvp.Key)
            .ToList();
        var result = keysToRemove
            .Select(k => (k.Sid, k.Path, k.IsDeny, k.IsTraverseOnly))
            .ToList();
        foreach (var key in keysToRemove)
            _grantConfigMap.Remove(key);
        return result;
    }

    private readonly record struct GrantKey(string Sid, string Path, bool IsDeny, bool IsTraverseOnly)
    {
        public GrantKey(string sid, GrantedPathEntry entry)
            : this(sid, entry.Path, entry.IsDeny, entry.IsTraverseOnly)
        {
        }

        public bool Equals(GrantKey other) =>
            string.Equals(Sid, other.Sid, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(Path, other.Path, StringComparison.OrdinalIgnoreCase) &&
            IsDeny == other.IsDeny &&
            IsTraverseOnly == other.IsTraverseOnly;

        public override int GetHashCode() =>
            HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(Sid),
                StringComparer.OrdinalIgnoreCase.GetHashCode(Path),
                IsDeny,
                IsTraverseOnly);
    }
}