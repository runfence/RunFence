using RunFence.Core.Models;

namespace RunFence.Persistence;

/// <summary>
/// Tracks which additional config file each grant belongs to.
/// Exposes per-entry lookup, mutation, and batch unregistration methods.
/// </summary>
public interface IGrantConfigTracker
{
    void AssignGrant(string sid, GrantedPathEntry entry, string? configPath);
    void RemoveGrant(string sid, GrantedPathEntry entry);
    string? GetGrantConfigPath(string sid, GrantedPathEntry entry);

    /// <summary>
    /// Returns true if the grant is NOT tracked in any extra config (belongs to main config).
    /// </summary>
    bool IsInMainConfig(string sid, GrantedPathEntry entry);

    /// <summary>
    /// Returns filtered account entries containing only grants that belong to
    /// <paramref name="configPath"/> (non-null = additional config).
    /// Returns null if there are no matching entries.
    /// </summary>
    List<AppConfigAccountEntry>? FilterGrantsForConfig(List<AccountEntry> accounts, string configPath);

    /// <summary>
    /// Removes all grant entries that belong to <paramref name="configPath"/> from the tracker.
    /// Returns the keys that were removed so callers can update the database.
    /// </summary>
    List<(string Sid, string Path, bool IsDeny, bool IsTraverseOnly)> UnregisterConfigGrants(string configPath);
}