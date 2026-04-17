using RunFence.Core.Models;
using RunFence.Persistence;

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

    /// <summary>
    /// Entries whose rights/mode changed (need NTFS update). Keyed by (normalizedPath, originalIsDeny)
    /// where <c>originalIsDeny</c> is the entry's <see cref="GrantedPathEntry.IsDeny"/> value as committed
    /// in the DB (i.e. the pre-switch mode for mode-switched entries). The entry is NOT mutated until Apply.
    /// Value: <see cref="PendingModification"/> where <c>WasIsDeny</c> is the IsDeny value from NTFS before
    /// any mode switch, <c>WasOwn</c> is the Own value at the time the entry was first modified, and
    /// <c>NewIsDeny</c>/<c>NewRights</c> carry the desired new state to apply without premature mutation.
    /// </summary>
    public Dictionary<(string Path, bool IsDeny), PendingModification> PendingModifications { get; } = new(PathKeyComparer);

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

    /// <summary>
    /// Returns true if a pending config-section move exists for the given path and IsDeny.
    /// Also checks the effective IsDeny (from a pending mode switch) in case the config move
    /// was stored under the new mode key due to a simultaneous mode switch.
    /// </summary>
    public bool IsPendingConfigMove(string path, bool isDeny)
    {
        if (PendingConfigMoves.ContainsKey((path, isDeny)))
            return true;
        // Check effective mode in case a mode switch re-keyed the config move to the new mode.
        if (PendingModifications.TryGetValue((path, isDeny), out var mod))
            return PendingConfigMoves.ContainsKey((path, mod.NewIsDeny));
        return false;
    }

    /// <summary>
    /// Returns the effective IsDeny for an entry, accounting for any pending mode switch.
    /// Uses the entry's original DB IsDeny as the lookup key, and returns <see cref="PendingModification.NewIsDeny"/>
    /// if a pending modification exists, otherwise the entry's own <see cref="GrantedPathEntry.IsDeny"/>.
    /// </summary>
    public bool GetEffectiveIsDeny(GrantedPathEntry entry)
    {
        if (PendingModifications.TryGetValue((entry.Path, entry.IsDeny), out var mod))
            return mod.NewIsDeny;
        return entry.IsDeny;
    }

    /// <summary>
    /// Returns the effective SavedRights for an entry, accounting for any pending rights change.
    /// Uses the entry's original DB IsDeny as the lookup key, and returns <see cref="PendingModification.NewRights"/>
    /// if a pending modification with non-null NewRights exists, otherwise the entry's own <see cref="GrantedPathEntry.SavedRights"/>.
    /// </summary>
    public SavedRightsState? GetEffectiveRights(GrantedPathEntry entry)
    {
        if (PendingModifications.TryGetValue((entry.Path, entry.IsDeny), out var mod) && mod.NewRights != null)
            return mod.NewRights;
        return entry.SavedRights;
    }

    /// <summary>
    /// Returns the effective config path for a grant entry, accounting for any pending
    /// config-section move. For grant entries (<paramref name="entry"/>.IsTraverseOnly false),
    /// checks <see cref="PendingConfigMoves"/> under the effective (post-mode-switch) key.
    /// For traverse entries, checks <see cref="PendingTraverseConfigMoves"/>.
    /// Falls back to <paramref name="grantConfigTracker"/> when no pending move exists.
    /// </summary>
    public string? GetEffectiveConfigPath(GrantedPathEntry entry, IGrantConfigTracker grantConfigTracker, string sid)
    {
        if (!entry.IsTraverseOnly)
        {
            var effectiveIsDeny = GetEffectiveIsDeny(entry);
            var key = (Path.GetFullPath(entry.Path), effectiveIsDeny);
            if (PendingConfigMoves.TryGetValue(key, out var pendingTarget))
                return pendingTarget;
        }
        else
        {
            var path = Path.GetFullPath(entry.Path);
            if (PendingTraverseConfigMoves.TryGetValue(path, out var pendingTarget))
                return pendingTarget;
        }

        return grantConfigTracker.GetGrantConfigPath(sid, entry);
    }

    public bool IsPendingAdd(string path, bool isDeny) => PendingAdds.ContainsKey((path, isDeny));

    public GrantedPathEntry? FindPendingAdd(string path, bool isDeny) =>
        PendingAdds.GetValueOrDefault((path, isDeny));

    public bool IsPendingRemove(string path, bool isDeny) => PendingRemoves.ContainsKey((path, isDeny));

    public bool IsPendingModification(string path, bool isDeny) => PendingModifications.ContainsKey((path, isDeny));

    public bool IsPendingTraverseAdd(string path) => PendingTraverseAdds.ContainsKey(path);

    public bool IsPendingTraverseRemove(string path) => PendingTraverseRemoves.ContainsKey(path);

    public bool IsUntrackGrant(string path, bool isDeny) => PendingUntrackGrants.ContainsKey((path, isDeny));

    public bool IsUntrackTraverse(string path) => PendingUntrackTraverse.ContainsKey(path);

    public bool IsPendingTraverseConfigMove(string path) => PendingTraverseConfigMoves.ContainsKey(path);

    /// <summary>
    /// Returns true if any pending change (add, modification, or config-section move) exists
    /// for the given path and IsDeny. Centralizes the three-way check used in row rendering.
    /// </summary>
    public bool IsPendingGrantChange(string path, bool isDeny) =>
        IsPendingAdd(path, isDeny) ||
        IsPendingModification(path, isDeny) ||
        IsPendingConfigMove(path, isDeny);

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
/// Represents a pending modification to an existing grant entry, capturing both the original state
/// needed to correctly revert or reset ownership during apply and the new desired state to apply
/// without mutating the live DB entry prematurely.
/// </summary>
/// <param name="Entry">The live DB entry (not mutated until Apply).</param>
/// <param name="WasIsDeny">The IsDeny value from NTFS before any mode switch.</param>
/// <param name="WasOwn">The Own value at the time the entry was first tracked as a modification.</param>
/// <param name="NewIsDeny">The desired IsDeny value to apply; equals entry.IsDeny for pure rights-only changes.</param>
/// <param name="NewRights">The desired SavedRights to apply; null means no pending rights change (entry.SavedRights remains in effect).</param>
public record PendingModification(GrantedPathEntry Entry, bool WasIsDeny, bool WasOwn, bool NewIsDeny, SavedRightsState? NewRights);

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