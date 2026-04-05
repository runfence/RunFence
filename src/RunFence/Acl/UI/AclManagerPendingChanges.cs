using RunFence.Core.Models;

namespace RunFence.Acl.UI;

/// <summary>
/// Tracks pending ACL Manager changes that have not yet been applied to NTFS.
/// Separates adds, removes, modifications, and traverse operations.
/// All path keys are case-insensitive (Windows paths are case-insensitive).
/// </summary>
public class AclManagerPendingChanges
{
    private static readonly GrantPathKeyComparer PathKeyComparer = new();

    /// <summary>Entries to add (not yet on NTFS). Keyed by (normalizedPath, isDeny).</summary>
    public Dictionary<(string Path, bool IsDeny), GrantedPathEntry> PendingAdds { get; } = new(PathKeyComparer);

    /// <summary>Entries to remove (ACEs still on NTFS). Keyed by (normalizedPath, isDeny).</summary>
    public Dictionary<(string Path, bool IsDeny), GrantedPathEntry> PendingRemoves { get; } = new(PathKeyComparer);

    /// <summary>Entries whose rights/mode changed (need NTFS update). Keyed by (normalizedPath, isDeny).</summary>
    public Dictionary<(string Path, bool IsDeny), GrantedPathEntry> PendingModifications { get; } = new(PathKeyComparer);

    /// <summary>Traverse entries to add. Keyed by normalizedPath.</summary>
    public Dictionary<string, GrantedPathEntry> PendingTraverseAdds { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Traverse entries to remove. Keyed by normalizedPath.</summary>
    public Dictionary<string, GrantedPathEntry> PendingTraverseRemoves { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Existing traverse entries that need re-granting (Fix ACLs on traverse tab). Keyed by normalizedPath.</summary>
    public Dictionary<string, GrantedPathEntry> PendingTraverseFixes { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Grant entries to remove from DB only (no NTFS ACE removal). Keyed by (normalizedPath, isDeny).</summary>
    public Dictionary<(string Path, bool IsDeny), GrantedPathEntry> PendingUntrackGrants { get; } = new(PathKeyComparer);

    /// <summary>Traverse entries to remove from DB only (no NTFS ACE removal). Keyed by normalizedPath.</summary>
    public Dictionary<string, GrantedPathEntry> PendingUntrackTraverse { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Deferred config-section moves for grant entries. Key=(normalizedPath, isDeny), value=target config path (null=main).</summary>
    public Dictionary<(string Path, bool IsDeny), string?> PendingConfigMoves { get; } = new(PathKeyComparer);

    /// <summary>Deferred config-section moves for traverse entries. Key=normalizedPath, value=target config path (null=main).</summary>
    public Dictionary<string, string?> PendingTraverseConfigMoves { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool HasPendingChanges => PendingAdds.Count + PendingRemoves.Count +
        PendingModifications.Count + PendingTraverseAdds.Count + PendingTraverseRemoves.Count +
        PendingTraverseFixes.Count + PendingUntrackGrants.Count + PendingUntrackTraverse.Count +
        PendingConfigMoves.Count + PendingTraverseConfigMoves.Count > 0;

    public bool IsPendingConfigMove(string path, bool isDeny) => PendingConfigMoves.ContainsKey((path, isDeny));

    public bool IsPendingTraverseConfigMove(string path) => PendingTraverseConfigMoves.ContainsKey(path);

    public bool IsPendingAdd(string path, bool isDeny) => PendingAdds.ContainsKey((path, isDeny));

    public GrantedPathEntry? FindPendingAdd(string path, bool isDeny) =>
        PendingAdds.GetValueOrDefault((path, isDeny));

    public bool IsPendingRemove(string path, bool isDeny) => PendingRemoves.ContainsKey((path, isDeny));

    public bool IsPendingModification(string path, bool isDeny) => PendingModifications.ContainsKey((path, isDeny));

    public bool IsPendingTraverseAdd(string path) => PendingTraverseAdds.ContainsKey(path);

    public bool IsPendingTraverseRemove(string path) => PendingTraverseRemoves.ContainsKey(path);

    public bool IsUntrackGrant(string path, bool isDeny) => PendingUntrackGrants.ContainsKey((path, isDeny));

    public bool IsUntrackTraverse(string path) => PendingUntrackTraverse.ContainsKey(path);

    /// <summary>
    /// Returns true if an entry with the given path and mode already exists either as a
    /// pending add or as a committed DB entry that is not pending removal or untrack.
    /// </summary>
    public bool ExistsInDbOrPending(AppDatabase database, string sid, string normalizedPath, bool isDeny)
    {
        if (IsPendingAdd(normalizedPath, isDeny))
            return true;
        var entries = database.GetAccount(sid)?.Grants;
        return entries != null &&
               entries.Any(e => string.Equals(e.Path, normalizedPath, StringComparison.OrdinalIgnoreCase) &&
                                e.IsDeny == isDeny && !e.IsTraverseOnly &&
                                !IsPendingRemove(normalizedPath, isDeny) &&
                                !IsUntrackGrant(normalizedPath, isDeny));
    }

    /// <summary>
    /// Returns true if a traverse entry with the given path already exists either as a
    /// pending add or as a committed DB entry that is not pending removal (or untrack, when
    /// <paramref name="checkUntrack"/> is true).
    /// </summary>
    public bool ExistsTraverseInDbOrPending(AppDatabase database, string sid, string normalizedPath, bool checkUntrack = true)
    {
        if (IsPendingTraverseAdd(normalizedPath))
            return true;
        var entries = database.GetAccount(sid)?.Grants;
        return entries != null &&
               entries.Any(e => e.IsTraverseOnly &&
                                string.Equals(e.Path, normalizedPath, StringComparison.OrdinalIgnoreCase) &&
                                !IsPendingTraverseRemove(normalizedPath) &&
                                (!checkUntrack || !IsUntrackTraverse(normalizedPath)));
    }

    public void Clear()
    {
        PendingAdds.Clear();
        PendingRemoves.Clear();
        PendingModifications.Clear();
        PendingTraverseAdds.Clear();
        PendingTraverseRemoves.Clear();
        PendingTraverseFixes.Clear();
        PendingUntrackGrants.Clear();
        PendingUntrackTraverse.Clear();
        PendingConfigMoves.Clear();
        PendingTraverseConfigMoves.Clear();
    }
}

/// <summary>
/// Case-insensitive comparer for (Path, IsDeny) grant path keys.
/// </summary>
public sealed class GrantPathKeyComparer : IEqualityComparer<(string Path, bool IsDeny)>
{
    public bool Equals((string Path, bool IsDeny) x, (string Path, bool IsDeny) y) =>
        string.Equals(x.Path, y.Path, StringComparison.OrdinalIgnoreCase) && x.IsDeny == y.IsDeny;

    public int GetHashCode((string Path, bool IsDeny) obj) =>
        HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Path), obj.IsDeny);
}