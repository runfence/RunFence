using RunFence.Core.Models;

namespace RunFence.Acl.UI;

/// <summary>
/// Compares actual NTFS rights (from ReadRights) against saved rights on a <see cref="GrantedPathEntry"/>.
/// Used to detect drift (yellow row indicator) in the ACL Manager.
/// </summary>
public class SavedRightsComparer
{
    public static readonly SavedRightsComparer Instance = new();

    private SavedRightsComparer()
    {
    }

    /// <summary>
    /// Compares actual NTFS rights against saved rights on the entry.
    /// Returns true if they match (no drift).
    /// Returns false if <see cref="GrantedPathEntry.SavedRights"/> is null (not yet populated — triggers auto-populate).
    /// Also returns false if the ACE count for the entry's mode is 0 (no ACE) or &gt; 1 (duplicates).
    /// </summary>
    public bool MatchesSavedRights(GrantedPathEntry entry, GrantRightsState state, bool isContainer)
    {
        if (entry.SavedRights == null)
            return false;

        var saved = entry.SavedRights;

        if (!entry.IsDeny)
        {
            // No allow ACE at all → always mismatch (Read is always-on but not tracked in GrantRightsState)
            if (state.DirectAllowAceCount == 0)
                return false;

            // Duplicate allow ACEs → always mismatch (should be exactly 1 per mode)
            if (state.DirectAllowAceCount > 1)
                return false;

            // Allow mode: compare Execute, Write, Special. Read is always-on (no AllowRead in GrantRightsState).
            if (saved.Execute != (state.AllowExecute == CheckState.Checked))
                return false;
            if (saved.Write != (state.AllowWrite == CheckState.Checked))
                return false;
            if (saved.Special != (state.AllowSpecial == CheckState.Checked))
                return false;

            // Own comparison (skip for containers)
            if (!isContainer)
            {
                bool actualOwner = state.IsAccountOwner == CheckState.Checked;
                if (saved.Own != actualOwner)
                    return false;
            }
        }
        else
        {
            // No deny ACE at all → always mismatch (Write+Special are always-on but not re-compared here)
            if (state.DirectDenyAceCount == 0)
                return false;

            // Duplicate deny ACEs → always mismatch (should be exactly 1 per mode)
            if (state.DirectDenyAceCount > 1)
                return false;

            // Deny mode: compare Execute and Read only (Write+Special are always-on, skip)
            if (saved.Execute != (state.DenyExecute == CheckState.Checked))
                return false;
            if (saved.Read != (state.DenyRead == CheckState.Checked))
                return false;

            // Own comparison (skip for containers)
            // Deny+unchecked (saved.Own == false) → never a mismatch regardless of actual owner
            // Deny+checked (saved.Own == true, wants admin to own) but this SID is the owner → mismatch
            // Deny+checked but owner is someone else (admin or third party) → NOT a mismatch
            if (!isContainer && saved.Own)
            {
                if (state.IsAccountOwner == CheckState.Checked)
                    return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Auto-populates <see cref="GrantedPathEntry.SavedRights"/> from the current NTFS state
    /// for entries where <see cref="GrantedPathEntry.SavedRights"/> is null.
    /// </summary>
    /// <param name="entries">All grant entries to consider.</param>
    /// <param name="readRights">Delegate that returns current NTFS state, or null if the path does not exist.</param>
    /// <param name="isContainer">True when the account is an app container (Own is always false).</param>
    /// <returns>List of entries that were populated (caller should persist to DB).</returns>
    public List<GrantedPathEntry> AutoPopulateMissingSavedRights(
        IEnumerable<GrantedPathEntry> entries,
        Func<GrantedPathEntry, GrantRightsState?> readRights,
        bool isContainer)
    {
        var populated = new List<GrantedPathEntry>();

        foreach (var entry in entries)
        {
            if (entry.SavedRights != null)
                continue;

            var state = readRights(entry);
            if (state == null)
                continue;

            entry.SavedRights = BuildSavedRights(entry, state, isContainer);
            populated.Add(entry);
        }

        return populated;
    }

    private static SavedRightsState BuildSavedRights(GrantedPathEntry entry, GrantRightsState state, bool isContainer)
        => FromNtfsState(state, entry.IsDeny, isContainer);

    /// <summary>
    /// Builds a <see cref="SavedRightsState"/> from the current NTFS state for the given mode.
    /// </summary>
    public static SavedRightsState FromNtfsState(GrantRightsState state, bool isDeny, bool isContainer)
    {
        if (!isDeny)
        {
            return new SavedRightsState(
                Execute: state.AllowExecute == CheckState.Checked,
                Write: state.AllowWrite == CheckState.Checked,
                Read: true, // always on in allow mode
                Special: state.AllowSpecial == CheckState.Checked,
                Own: !isContainer && state.IsAccountOwner == CheckState.Checked);
        }

        return new SavedRightsState(
            Execute: state.DenyExecute == CheckState.Checked,
            Write: true, // always on in deny mode
            Read: state.DenyRead == CheckState.Checked,
            Special: true, // always on in deny mode
            Own: !isContainer && state.IsAdminOwner);
    }
}