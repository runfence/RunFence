namespace RunFence.Account;

/// <summary>
/// Encapsulates the SID name resolve-and-cache protocol used across the codebase:
/// live OS resolution via <see cref="RunFence.Core.ISidResolver"/>, fallback through the
/// central <see cref="RunFence.Core.Models.AppDatabase.SidNames"/> map, and persistence of
/// newly resolved names back into the database via <see cref="RunFence.Core.Models.AppDatabase.UpdateSidName"/>.
///
/// <para>Use <see cref="GetDisplayName"/> when only reading a display name for UI purposes.
/// Use <see cref="ResolveAndCache"/> when a name has just been resolved (e.g., after account creation)
/// and must be persisted into the database cache. Use <see cref="UpdateName"/> to store a known name
/// directly (e.g., after an inline rename).</para>
/// </summary>
public interface ISidNameCacheService
{
    /// <summary>
    /// Resolves and returns the display name for <paramref name="sid"/> using the standard fallback chain:
    /// live OS lookup → registry profile → central SidNames map → raw SID.
    /// Does not update the database.
    /// </summary>
    string GetDisplayName(string sid);

    /// <summary>
    /// Resolves the display name for <paramref name="sid"/>, stores it in the database cache,
    /// and returns the resolved name. When the OS lookup returns no result, uses <paramref name="fallbackName"/>
    /// if provided, otherwise falls back to the raw SID.
    /// </summary>
    string ResolveAndCache(string sid, string? fallbackName = null);

    /// <summary>
    /// Directly stores <paramref name="name"/> for <paramref name="sid"/> in the database cache.
    /// Used when the name is already known (e.g., after an inline rename).
    /// </summary>
    void UpdateName(string sid, string name);
}