using RunFence.Core;
using RunFence.Persistence;

namespace RunFence.Account;

/// <summary>
/// Implements <see cref="ISidNameCacheService"/> by delegating to <see cref="SidNameResolver"/>
/// for display-name resolution and to <see cref="IDatabaseProvider"/> for cache persistence.
/// </summary>
public class SidNameCacheService(ISidResolver sidResolver, IDatabaseProvider databaseProvider)
    : ISidNameCacheService
{
    public string GetDisplayName(string sid)
    {
        var preResolved = sidResolver.TryResolveName(sid);
        return SidNameResolver.GetDisplayName(sid, preResolved, sidResolver, databaseProvider.GetDatabase().SidNames);
    }

    public string ResolveAndCache(string sid, string? fallbackName = null)
    {
        var resolved = sidResolver.TryResolveName(sid) ?? fallbackName ?? sid;
        databaseProvider.GetDatabase().UpdateSidName(sid, resolved);
        return resolved;
    }

    public void UpdateName(string sid, string name)
        => databaseProvider.GetDatabase().UpdateSidName(sid, name);
}