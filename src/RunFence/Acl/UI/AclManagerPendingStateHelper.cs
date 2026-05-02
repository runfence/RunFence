using RunFence.Core.Models;

namespace RunFence.Acl.UI;

/// <summary>
/// Computes and tracks pending modifications from grid state changes, decoupling
/// "what changes to track" from "how to display grants in the grid."
/// Initialized via <see cref="Initialize"/> with the dialog's shared <see cref="AclManagerPendingChanges"/>.
/// </summary>
public class AclManagerPendingStateHelper
{
    private AclManagerPendingChanges _pending = null!;

    public void Initialize(AclManagerPendingChanges pending)
    {
        _pending = pending;
    }

    /// <summary>
    /// Computes the new <see cref="PendingModification"/> for a DB entry when its mode or rights change.
    /// Does not mutate <paramref name="entry"/>; all changes are recorded in the shared pending state.
    /// </summary>
    public void ComputePendingModification(GrantedPathEntry entry, bool newIsDeny, SavedRightsState? newRights)
    {
        var dbKey = (entry.Path, entry.IsDeny);

        // Preserve the true original NTFS wasIsDeny across multiple mode switches: if there is
        // already a pending modification under dbKey, its WasIsDeny reflects the actual NTFS state.
        // This ensures double-switch (Allow→Deny→Allow) does not try to revert a non-existent ACE.
        bool originalWasIsDeny = _pending.PendingModifications.TryGetValue(dbKey, out var existingMod)
            ? existingMod.WasIsDeny
            : entry.IsDeny;

        bool ownValue = _pending.GetEffectiveRights(entry)?.Own ?? false;
        bool wasOwn = existingMod?.WasOwn ?? (entry.SavedRights?.Own == true);

        _pending.PendingModifications[dbKey] = new PendingModification(
            entry, WasIsDeny: originalWasIsDeny, WasOwn: wasOwn,
            NewIsDeny: newIsDeny, NewRights: newRights ?? SavedRightsState.DefaultForMode(newIsDeny, own: ownValue));
    }

    /// <summary>
    /// Re-keys the pending config move for a mode switch: checks the previous effective key
    /// (which may already have been re-keyed by a prior switch) and moves it to the new key.
    /// </summary>
    public void TrackModeChange(GrantedPathEntry entry, bool newIsDeny)
    {
        var dbKey = (entry.Path, entry.IsDeny);
        _pending.PendingModifications.TryGetValue(dbKey, out var existingMod);

        // Re-key the config move from wherever it currently lives to the new mode key.
        // On a first switch the move lives at dbKey; on a subsequent switch it already lives
        // at the previous effective mode key (existingMod.NewIsDeny), so check that key first.
        var prevEffectiveKey = existingMod != null ? (entry.Path, existingMod.NewIsDeny) : dbKey;
        var newPendingAddKey = (entry.Path, newIsDeny);
        bool hadConfigMove = _pending.PendingConfigMoves.Remove(prevEffectiveKey, out var configTarget);
        if (!hadConfigMove && prevEffectiveKey != dbKey)
            hadConfigMove = _pending.PendingConfigMoves.Remove(dbKey, out configTarget);
        if (hadConfigMove)
            _pending.PendingConfigMoves[newPendingAddKey] = configTarget;
    }
}
