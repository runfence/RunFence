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

    public void ComputePendingModification(GrantedPathEntry entry, bool newIsDeny, SavedRightsState? newRights)
    {
        var hasExistingMod = _pending.Grants.TryGetPendingModification(entry.Path, entry.IsDeny, out var existingMod);
        bool originalWasIsDeny = hasExistingMod
            ? existingMod!.WasIsDeny
            : entry.IsDeny;

        bool ownValue = _pending.Grants.GetEffectiveRights(entry)?.Own ?? false;
        bool wasOwn = existingMod?.WasOwn ?? (entry.SavedRights?.Own == true);

        _pending.Grants.ModifyGrant(entry, new PendingModification(
            entry,
            WasIsDeny: originalWasIsDeny,
            WasOwn: wasOwn,
            NewIsDeny: newIsDeny,
            NewRights: newRights ?? SavedRightsState.DefaultForMode(newIsDeny, own: ownValue),
            WasRights: existingMod?.WasRights ?? entry.SavedRights,
            WasPreviousSaclLabel: existingMod?.WasPreviousSaclLabel ?? entry.PreviousSaclLabel));
    }

    public void TrackModeChange(GrantedPathEntry entry, bool newIsDeny)
    {
        var dbKey = (entry.Path, entry.IsDeny);
        _pending.Grants.TryGetPendingModification(entry.Path, entry.IsDeny, out var existingMod);

        var prevEffectiveKey = existingMod != null ? (entry.Path, existingMod.NewIsDeny) : dbKey;
        var updatedEntry = entry.Clone();
        updatedEntry.IsDeny = newIsDeny;

        bool hadConfigMove = _pending.Grants.RekeyGrantConfigMove(prevEffectiveKey.Path, prevEffectiveKey.Item2, updatedEntry);
        if (!hadConfigMove && prevEffectiveKey != dbKey)
            hadConfigMove = _pending.Grants.RekeyGrantConfigMove(dbKey.Path, dbKey.IsDeny, updatedEntry);
        if (!hadConfigMove)
            _ = _pending.Grants.RekeyGrantConfigMove(entry.Path, !newIsDeny, updatedEntry);
    }
}
