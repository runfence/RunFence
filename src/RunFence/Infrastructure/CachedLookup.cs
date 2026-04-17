namespace RunFence.Infrastructure;

/// <summary>
/// Generic keyed cache with TTL expiry. Thread-safe via an internal lock.
/// Encapsulates the check-TTL-fetch-store pattern repeated in <see cref="LocalGroupMembershipService"/>.
/// </summary>
public class CachedLookup<TKey, TValue> where TKey : notnull
{
    private readonly TimeSpan _ttl;
    private readonly Lock _lock = new();
    private readonly Dictionary<TKey, (TValue Data, DateTime Time)> _cache;

    public CachedLookup(TimeSpan ttl)
    {
        _ttl = ttl;
        _cache = new Dictionary<TKey, (TValue, DateTime)>();
    }

    public CachedLookup(TimeSpan ttl, IEqualityComparer<TKey> comparer)
    {
        _ttl = ttl;
        _cache = new Dictionary<TKey, (TValue, DateTime)>(comparer);
    }

    /// <summary>
    /// Returns the cached value for <paramref name="key"/> if still fresh,
    /// or calls <paramref name="fetch"/> to obtain and cache a new value.
    /// </summary>
    public TValue Get(TKey key, Func<TValue> fetch)
    {
        lock (_lock)
        {
            if (_cache.TryGetValue(key, out var cached) && DateTime.UtcNow - cached.Time < _ttl)
                return cached.Data;
        }
        var result = fetch();
        lock (_lock)
        {
            if (!_cache.TryGetValue(key, out var existing) || DateTime.UtcNow - existing.Time >= _ttl)
                _cache[key] = (result, DateTime.UtcNow);
        }
        return result;
    }

    /// <summary>Removes the cached entry for <paramref name="key"/>.</summary>
    public void Invalidate(TKey key)
    {
        lock (_lock) { _cache.Remove(key); }
    }

    /// <summary>Removes the cached entries for all provided keys.</summary>
    public void InvalidateAll(IEnumerable<TKey> keys)
    {
        lock (_lock) { foreach (var key in keys) _cache.Remove(key); }
    }

    /// <summary>Clears the entire cache.</summary>
    public void Clear()
    {
        lock (_lock) { _cache.Clear(); }
    }
}
