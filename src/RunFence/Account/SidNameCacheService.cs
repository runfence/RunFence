using RunFence.Acl;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Persistence;

namespace RunFence.Account;

/// <summary>
/// Implements <see cref="ISidNameCacheService"/> by delegating to <see cref="SidNameResolver"/>
/// for display-name resolution and to <see cref="IDatabaseProvider"/> for cache persistence.
/// </summary>
public class SidNameCacheService(ISidResolver sidResolver, IProfilePathResolver profilePathResolver, IDatabaseProvider databaseProvider)
    : ISidNameCacheService
{
    private Dictionary<string, string> _containerDisplayNames = new(StringComparer.OrdinalIgnoreCase);
    private int _cachedContainerCount = -1;

    public string GetDisplayName(string sid)
    {
        if (AclHelper.IsLowIntegritySid(sid))
            return "Low Integrity"; // Well-known label SID — no OS lookup needed

        var db = databaseProvider.GetDatabase();
        if (AclHelper.IsContainerSid(sid))
        {
            if (db.AppContainers.Count != _cachedContainerCount)
                RebuildContainerCache(db.AppContainers);
            if (_containerDisplayNames.TryGetValue(sid, out var displayName))
                return displayName;
        }

        var preResolved = sidResolver.TryResolveName(sid);
        return SidNameResolver.GetDisplayName(sid, preResolved, sidResolver, db.SidNames, profilePathResolver);
    }

    private void RebuildContainerCache(List<AppContainerEntry> containers)
    {
        _containerDisplayNames = containers
            .Where(c => !string.IsNullOrEmpty(c.Sid))
            .ToDictionary(
                c => c.Sid,
                c => !string.IsNullOrEmpty(c.DisplayName) ? c.DisplayName : c.Name,
                StringComparer.OrdinalIgnoreCase);
        _cachedContainerCount = containers.Count;
    }

    public string ResolveAndCache(string sid, string? fallbackName = null)
    {
        if (AclHelper.IsContainerSid(sid) || AclHelper.IsLowIntegritySid(sid))
            return fallbackName ?? sid;
        var resolved = sidResolver.TryResolveName(sid) ?? fallbackName ?? sid;
        databaseProvider.GetDatabase().UpdateSidName(sid, resolved);
        return resolved;
    }

    public void UpdateName(string sid, string name)
        => databaseProvider.GetDatabase().UpdateSidName(sid, name);
}