using System.Security.AccessControl;
using RunFence.Core.Models;

namespace RunFence.Acl.UI;

/// <summary>
/// Compares actual NTFS rights (from ReadGrantState) against saved rights on a <see cref="GrantedPathEntry"/>.
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
    /// <para>
    /// <paramref name="isFolder"/> is accepted for API consistency with <see cref="FromNtfsState"/>
    /// but does not affect comparison logic — <see cref="GrantRightsState"/> already exposes
    /// per-right booleans independent of path type.
    /// </para>
    /// </summary>
    public bool MatchesSavedRights(GrantedPathEntry entry, GrantRightsState state, bool isContainer, bool isFolder = true)
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
            if (saved.Execute != (state.AllowExecute == RightCheckState.Checked))
                return false;
            if (saved.Write != (state.AllowWrite == RightCheckState.Checked))
                return false;
            if (saved.Special != (state.AllowSpecial == RightCheckState.Checked))
                return false;

            // Own comparison (skip for containers)
            if (!isContainer)
            {
                bool actualOwner = state.IsAccountOwner == RightCheckState.Checked;
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
            if (saved.Execute != (state.DenyExecute == RightCheckState.Checked))
                return false;
            if (saved.Read != (state.DenyRead == RightCheckState.Checked))
                return false;

            // Own comparison (skip for containers)
            // Deny+unchecked (saved.Own == false) → never a mismatch regardless of actual owner
            // Deny+checked (saved.Own == true, wants admin to own) but this SID is the owner → mismatch
            // Deny+checked but owner is someone else (admin or third party) → NOT a mismatch
            if (!isContainer && saved.Own)
            {
                if (state.IsAccountOwner == RightCheckState.Checked)
                    return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Auto-populates <see cref="GrantedPathEntry.SavedRights"/> from the current NTFS state
    /// for entries where <see cref="GrantedPathEntry.SavedRights"/> is null.
    /// Uses <see cref="GrantRightsMapper.FromNtfsRights"/> for accurate path-type-aware mapping.
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

            bool isFolder = Directory.Exists(entry.Path);
            entry.SavedRights = FromNtfsState(state, entry.IsDeny, isContainer, isFolder);
            populated.Add(entry);
        }

        return populated;
    }

    /// <summary>
    /// Builds a <see cref="SavedRightsState"/> from the current NTFS state for the given mode.
    /// Uses <see cref="GrantRightsMapper.FromNtfsRights"/> for accurate path-type-aware mapping.
    /// <paramref name="isFolder"/> controls which Write/Special masks are applied.
    /// For non-existent paths where isFolder cannot be determined, default to true (folder assumption).
    /// </summary>
    public static SavedRightsState FromNtfsState(GrantRightsState state, bool isDeny, bool isContainer, bool isFolder = true)
    {
        var allowRights = BuildAllowRights(state, isFolder);
        var denyRights = BuildDenyRights(state, isFolder);
        var ownerState = isContainer ? RightCheckState.Unchecked : state.IsAccountOwner;
        bool adminOwner = !isContainer && state.IsAdminOwner;
        return GrantRightsMapper.FromNtfsRights(allowRights, denyRights, isDeny, isFolder, ownerState, adminOwner);
    }

    private static FileSystemRights BuildAllowRights(GrantRightsState state, bool isFolder)
    {
        var result = GrantRightsMapper.ReadMask;
        if (state.AllowExecute == RightCheckState.Checked)
            result |= GrantRightsMapper.ExecuteMask;
        if (state.AllowWrite == RightCheckState.Checked)
            result |= isFolder ? GrantRightsMapper.WriteFolderMask : GrantRightsMapper.WriteFileMask;
        if (state.AllowSpecial == RightCheckState.Checked)
            result |= isFolder ? GrantRightsMapper.SpecialFolderMask : GrantRightsMapper.SpecialFileMask;
        return result;
    }

    private static FileSystemRights BuildDenyRights(GrantRightsState state, bool isFolder)
    {
        var result = (isFolder ? GrantRightsMapper.WriteFolderMask : GrantRightsMapper.WriteFileMask) |
                     (isFolder ? GrantRightsMapper.SpecialFolderMask : GrantRightsMapper.SpecialFileMask);
        if (state.DenyRead == RightCheckState.Checked)
            result |= GrantRightsMapper.ReadMask;
        if (state.DenyExecute == RightCheckState.Checked)
            result |= GrantRightsMapper.ExecuteMask;
        return result;
    }
}
