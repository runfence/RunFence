using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.Acl.UI;

/// <summary>
/// Renders individual grant rows in the ACL Manager grants grid, handling status coloring,
/// checkbox state display, and NTFS drift detection. Extracted from <see cref="AclManagerGrantsHelper"/>.
/// </summary>
public class AclManagerGrantRowRenderer(
    DataGridView grid,
    string sid,
    bool isContainer,
    IGrantedPathAclService aclService,
    ILoggingService log,
    IReadOnlyList<string> groupSids,
    AclManagerPendingChanges pending)
{
    private readonly SavedRightsComparer _comparer = SavedRightsComparer.Instance;

    public HashSet<GrantedPathEntry> FixableEntries { get; } = new();

    public void AddGrantRow(GrantedPathEntry entry)
    {
        bool isPendingChange = pending.IsPendingAdd(entry.Path, entry.IsDeny) ||
                               pending.IsPendingModification(entry.Path, entry.IsDeny) ||
                               pending.IsPendingConfigMove(entry.Path, entry.IsDeny);

        GrantRightsState state;
        PathAclStatus status;

        if (isPendingChange)
        {
            state = BuildStateFromSavedRights(entry);
            status = PathAclStatus.Available;
        }
        else
        {
            state = ReadRightsForEntry(entry);
            status = DetermineStatus(entry, state);
        }

        var row = new DataGridViewRow();
        row.CreateCells(grid);
        row.Tag = entry;

        int iIcon = grid.Columns[AclManagerGrantsHelper.ColIcon]!.Index;
        int iPath = grid.Columns[AclManagerGrantsHelper.ColPath]!.Index;
        int iMode = grid.Columns[AclManagerGrantsHelper.ColMode]!.Index;
        int iRead = grid.Columns[AclManagerGrantsHelper.ColRead]!.Index;
        int iExecute = grid.Columns[AclManagerGrantsHelper.ColExecute]!.Index;
        int iWrite = grid.Columns[AclManagerGrantsHelper.ColWrite]!.Index;
        int iSpecial = grid.Columns[AclManagerGrantsHelper.ColSpecial]!.Index;

        row.Cells[iIcon].Value = AclPathIconProvider.GetIcon(entry.Path);
        row.Cells[iPath].Value = entry.Path;
        row.Cells[iMode].Value = entry.IsDeny ? AclManagerGrantsHelper.ModeDeny : AclManagerGrantsHelper.ModeAllow;
        row.Cells[iMode].ReadOnly = status == PathAclStatus.Unavailable;

        PopulateRightsCells(row, entry, state, isPendingChange, iRead, iExecute, iWrite, iSpecial);

        if (!isContainer && grid.Columns.Contains(AclManagerGrantsHelper.ColOwner))
        {
            row.Cells[grid.Columns[AclManagerGrantsHelper.ColOwner]!.Index].Value = entry.IsDeny
                ? state.IsAdminOwner ? CheckState.Checked : CheckState.Unchecked
                : state.IsAccountOwner;
        }

        ApplyRowStatus(row, status, isPendingChange);
        grid.Rows.Add(row);
    }

    public void RefreshRow(DataGridViewRow row, GrantedPathEntry entry)
    {
        bool isPending = pending.IsPendingAdd(entry.Path, entry.IsDeny) ||
                         pending.IsPendingModification(entry.Path, entry.IsDeny) ||
                         pending.IsPendingConfigMove(entry.Path, entry.IsDeny);
        RefreshRow(row, entry, isPending);
    }

    public void RefreshRow(DataGridViewRow row, GrantedPathEntry entry, bool isPendingChange,
        Action<bool>? setSuppressed = null)
    {
        setSuppressed?.Invoke(true);
        try
        {
            GrantRightsState state;
            PathAclStatus status;

            if (isPendingChange)
            {
                state = BuildStateFromSavedRights(entry);
                status = PathAclStatus.Available;
            }
            else
            {
                state = ReadRightsForEntry(entry);
                status = DetermineStatus(entry, state);
            }

            if (entry.IsDeny)
            {
                row.Cells[AclManagerGrantsHelper.ColRead].Value = isPendingChange
                    ? state.DenyRead
                    : GetDisplayCheckState(entry.SavedRights?.Read, state.DenyRead);
                row.Cells[AclManagerGrantsHelper.ColRead].ReadOnly = false;
                row.Cells[AclManagerGrantsHelper.ColExecute].Value = isPendingChange
                    ? state.DenyExecute
                    : GetDisplayCheckState(entry.SavedRights?.Execute, state.DenyExecute);
                row.Cells[AclManagerGrantsHelper.ColExecute].ReadOnly = false;
                row.Cells[AclManagerGrantsHelper.ColWrite].Value = state.DenyWrite;
                row.Cells[AclManagerGrantsHelper.ColWrite].ReadOnly = true;
                row.Cells[AclManagerGrantsHelper.ColSpecial].Value = state.DenySpecial;
                row.Cells[AclManagerGrantsHelper.ColSpecial].ReadOnly = true;
            }
            else
            {
                row.Cells[AclManagerGrantsHelper.ColRead].Value = CheckState.Checked;
                row.Cells[AclManagerGrantsHelper.ColRead].ReadOnly = true;
                row.Cells[AclManagerGrantsHelper.ColExecute].Value = isPendingChange
                    ? state.AllowExecute
                    : GetDisplayCheckState(entry.SavedRights?.Execute, state.AllowExecute);
                row.Cells[AclManagerGrantsHelper.ColExecute].ReadOnly = false;
                row.Cells[AclManagerGrantsHelper.ColWrite].Value = isPendingChange
                    ? state.AllowWrite
                    : GetDisplayCheckState(entry.SavedRights?.Write, state.AllowWrite);
                row.Cells[AclManagerGrantsHelper.ColWrite].ReadOnly = false;
                row.Cells[AclManagerGrantsHelper.ColSpecial].Value = isPendingChange
                    ? state.AllowSpecial
                    : GetDisplayCheckState(entry.SavedRights?.Special, state.AllowSpecial);
                row.Cells[AclManagerGrantsHelper.ColSpecial].ReadOnly = false;
            }

            if (!isContainer && grid.Columns.Contains(AclManagerGrantsHelper.ColOwner))
                row.Cells[AclManagerGrantsHelper.ColOwner].Value = entry.IsDeny
                    ? state.IsAdminOwner ? CheckState.Checked : CheckState.Unchecked
                    : state.IsAccountOwner;

            ApplyRowStatus(row, status, isPendingChange);
        }
        finally
        {
            setSuppressed?.Invoke(false);
        }
    }

    /// <summary>
    /// Updates only the background color of a row to reflect pending-change status
    /// without re-reading NTFS rights.
    /// </summary>
    public void RefreshRowBackground(DataGridViewRow row, GrantedPathEntry entry)
    {
        bool isPending = pending.IsPendingAdd(entry.Path, entry.IsDeny) ||
                         pending.IsPendingModification(entry.Path, entry.IsDeny) ||
                         pending.IsPendingConfigMove(entry.Path, entry.IsDeny);

        if (isPending)
        {
            SetPendingRowColor(row);
            FixableEntries.Remove(entry);
        }
    }

    /// <summary>
    /// Marks a broken (yellow) grant entry as pending fix so it will be re-applied on Apply.
    /// Refreshes the row background to green (pending).
    /// Does NOT immediately call aclService.
    /// </summary>
    public void FixBrokenGrant(GrantedPathEntry entry, DataGridViewRow row)
    {
        var key = (entry.Path, entry.IsDeny);
        if (!pending.PendingAdds.ContainsKey(key))
            pending.PendingModifications[key] = entry;

        SetPendingRowColor(row);
        FixableEntries.Remove(entry);
    }

    /// <summary>
    /// Re-applies only the row status (background color, fixable set) without touching cell values.
    /// Used when a pending modification is reverted to restore the correct non-pending appearance.
    /// </summary>
    public void ReapplyRowStatus(DataGridViewRow row, GrantedPathEntry entry, GrantRightsState ntfsState)
    {
        var status = DetermineStatus(entry, ntfsState);
        ApplyRowStatus(row, status, isPendingChange: false);
    }

    /// <summary>
    /// Determines display status for an entry by comparing saved rights against NTFS state.
    /// </summary>
    private PathAclStatus DetermineStatus(GrantedPathEntry entry, GrantRightsState state)
    {
        var rawStatus = aclService.CheckPathStatus(entry.Path, sid, entry.IsDeny);
        if (rawStatus == PathAclStatus.Unavailable)
            return PathAclStatus.Unavailable;

        if (!_comparer.MatchesSavedRights(entry, state, isContainer))
            return PathAclStatus.Broken;

        return PathAclStatus.Available;
    }

    public GrantRightsState ReadRightsForEntry(GrantedPathEntry entry)
    {
        try
        {
            return aclService.ReadRights(entry.Path, sid, groupSids);
        }
        catch (Exception ex)
        {
            log.Warn($"Failed to read rights for '{entry.Path}': {ex.Message}");
            return new GrantRightsState(
                CheckState.Unchecked, CheckState.Unchecked, CheckState.Unchecked,
                CheckState.Unchecked, CheckState.Unchecked, CheckState.Unchecked, CheckState.Unchecked,
                CheckState.Unchecked, false, 0, 0);
        }
    }

    /// <summary>
    /// Returns null when the path is inaccessible, so callers can skip auto-population.
    /// </summary>
    public GrantRightsState? TryReadRightsForEntry(GrantedPathEntry entry)
    {
        try
        {
            return aclService.ReadRights(entry.Path, sid, groupSids);
        }
        catch (Exception ex)
        {
            log.Warn($"Failed to read rights for '{entry.Path}' (skipping auto-populate): {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Marks a row as pending (green).
    /// </summary>
    public static void SetPendingRowColor(DataGridViewRow row)
    {
        row.DefaultCellStyle.BackColor = Color.LightGreen;
        row.DefaultCellStyle.SelectionBackColor = Color.DarkSeaGreen;
    }

    /// <summary>
    /// Returns the display <see cref="CheckState"/> for a rights checkbox cell.
    /// </summary>
    private static CheckState GetDisplayCheckState(bool? savedValue, CheckState ntfsState)
    {
        if (savedValue == null)
            return ntfsState;
        if (!savedValue.Value && ntfsState == CheckState.Indeterminate)
            return CheckState.Indeterminate;
        return savedValue.Value ? CheckState.Checked : CheckState.Unchecked;
    }

    /// <summary>
    /// Builds a synthetic <see cref="GrantRightsState"/> from <see cref="GrantedPathEntry.SavedRights"/>
    /// so that pending entries can be displayed without an NTFS read.
    /// </summary>
    private static GrantRightsState BuildStateFromSavedRights(GrantedPathEntry entry)
    {
        var s = entry.SavedRights;
        if (s == null)
            return new GrantRightsState(
                CheckState.Unchecked, CheckState.Unchecked, CheckState.Unchecked,
                CheckState.Unchecked, CheckState.Unchecked, CheckState.Unchecked, CheckState.Unchecked,
                CheckState.Unchecked, false, 1, 0);

        if (!entry.IsDeny)
        {
            return new GrantRightsState(
                AllowExecute: s.Execute ? CheckState.Checked : CheckState.Unchecked,
                AllowWrite: s.Write ? CheckState.Checked : CheckState.Unchecked,
                AllowSpecial: s.Special ? CheckState.Checked : CheckState.Unchecked,
                DenyRead: CheckState.Unchecked,
                DenyExecute: CheckState.Unchecked,
                DenyWrite: CheckState.Unchecked,
                DenySpecial: CheckState.Unchecked,
                IsAccountOwner: s.Own ? CheckState.Checked : CheckState.Unchecked,
                IsAdminOwner: false,
                DirectAllowAceCount: 1,
                DirectDenyAceCount: 0);
        }

        return new GrantRightsState(
            AllowExecute: CheckState.Unchecked,
            AllowWrite: CheckState.Unchecked,
            AllowSpecial: CheckState.Unchecked,
            DenyRead: s.Read ? CheckState.Checked : CheckState.Unchecked,
            DenyExecute: s.Execute ? CheckState.Checked : CheckState.Unchecked,
            DenyWrite: CheckState.Checked,
            DenySpecial: CheckState.Checked,
            IsAccountOwner: CheckState.Unchecked,
            IsAdminOwner: s.Own,
            DirectAllowAceCount: 0,
            DirectDenyAceCount: 1);
    }

    private void ApplyRowStatus(DataGridViewRow row, PathAclStatus status, bool isPendingChange)
    {
        var entry = row.Tag as GrantedPathEntry;
        switch (status)
        {
            case PathAclStatus.Unavailable:
                row.DefaultCellStyle.ForeColor = Color.Gray;
                row.DefaultCellStyle.BackColor = Color.WhiteSmoke;
                row.DefaultCellStyle.SelectionBackColor = Color.Empty;
                foreach (DataGridViewCell cell in row.Cells)
                    if (cell.ColumnIndex != grid.Columns[AclManagerGrantsHelper.ColPath]?.Index)
                        cell.ReadOnly = true;
                if (entry != null)
                    FixableEntries.Remove(entry);
                break;

            case PathAclStatus.Broken:
                if (isPendingChange)
                {
                    SetPendingRowColor(row);
                    if (entry != null)
                        FixableEntries.Remove(entry);
                }
                else
                {
                    row.DefaultCellStyle.BackColor = Color.LightYellow;
                    row.DefaultCellStyle.SelectionBackColor = Color.Empty;
                    if (entry != null)
                        FixableEntries.Add(entry);
                }

                break;

            default:
                if (isPendingChange)
                    SetPendingRowColor(row);
                else
                {
                    row.DefaultCellStyle.BackColor = Color.Empty;
                    row.DefaultCellStyle.SelectionBackColor = Color.Empty;
                }

                if (entry != null)
                    FixableEntries.Remove(entry);
                break;
        }
    }

    private void PopulateRightsCells(DataGridViewRow row, GrantedPathEntry entry,
        GrantRightsState state, bool isPendingChange,
        int iRead, int iExecute, int iWrite, int iSpecial)
    {
        if (entry.IsDeny)
        {
            row.Cells[iRead].Value = isPendingChange
                ? state.DenyRead
                : GetDisplayCheckState(entry.SavedRights?.Read, state.DenyRead);
            row.Cells[iExecute].Value = isPendingChange
                ? state.DenyExecute
                : GetDisplayCheckState(entry.SavedRights?.Execute, state.DenyExecute);
            row.Cells[iWrite].Value = state.DenyWrite;
            row.Cells[iWrite].ReadOnly = true;
            row.Cells[iSpecial].Value = state.DenySpecial;
            row.Cells[iSpecial].ReadOnly = true;
        }
        else
        {
            row.Cells[iRead].Value = CheckState.Checked;
            row.Cells[iRead].ReadOnly = true;
            row.Cells[iExecute].Value = isPendingChange
                ? state.AllowExecute
                : GetDisplayCheckState(entry.SavedRights?.Execute, state.AllowExecute);
            row.Cells[iWrite].Value = isPendingChange
                ? state.AllowWrite
                : GetDisplayCheckState(entry.SavedRights?.Write, state.AllowWrite);
            row.Cells[iSpecial].Value = isPendingChange
                ? state.AllowSpecial
                : GetDisplayCheckState(entry.SavedRights?.Special, state.AllowSpecial);
        }
    }
}