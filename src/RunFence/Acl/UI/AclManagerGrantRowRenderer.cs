using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.Acl.UI;

/// <summary>
/// Renders individual grant rows in the ACL Manager grants grid, handling status coloring,
/// checkbox state display, and NTFS drift detection. Extracted from <see cref="AclManagerGrantsHelper"/>.
/// </summary>
public class AclManagerGrantRowRenderer(IPathGrantService pathGrantService, IAclPathIconProvider iconProvider, ILoggingService log)
{
    private readonly SavedRightsComparer _comparer = SavedRightsComparer.Instance;

    private DataGridView _grid = null!;
    private string _sid = null!;
    private bool _isContainer;
    private IReadOnlyList<string> _groupSids = null!;
    private AclManagerPendingChanges _pending = null!;

    public void Initialize(DataGridView grid, string sid, bool isContainer,
        IReadOnlyList<string> groupSids, AclManagerPendingChanges pending)
    {
        _grid = grid;
        _sid = sid;
        _isContainer = isContainer;
        _groupSids = groupSids;
        _pending = pending;
    }

    public HashSet<GrantedPathEntry> FixableEntries { get; } = new();

    public void AddGrantRow(GrantedPathEntry entry)
    {
        bool isPendingChange = _pending.IsPendingGrantChange(entry.Path, entry.IsDeny);

        // For pending-modification rows, use effective IsDeny/Rights from the pending mod rather
        // than the (unmodified) DB entry, so the display reflects what will be applied.
        bool effectiveIsDeny = _pending.GetEffectiveIsDeny(entry);
        SavedRightsState? effectiveRights = _pending.GetEffectiveRights(entry);

        GrantRightsState state;
        PathAclStatus status;

        if (isPendingChange)
        {
            state = BuildStateFromSavedRights(effectiveIsDeny, effectiveRights);
            status = PathAclStatus.Available;
        }
        else
        {
            state = ReadRightsForEntry(entry);
            status = DetermineStatus(entry, state);
        }

        var row = new DataGridViewRow();
        row.CreateCells(_grid);
        row.Tag = entry;

        int iIcon = _grid.Columns[AclManagerGrantsHelper.ColIcon]!.Index;
        int iPath = _grid.Columns[AclManagerGrantsHelper.ColPath]!.Index;
        int iMode = _grid.Columns[AclManagerGrantsHelper.ColMode]!.Index;
        int iRead = _grid.Columns[AclManagerGrantsHelper.ColRead]!.Index;
        int iExecute = _grid.Columns[AclManagerGrantsHelper.ColExecute]!.Index;
        int iWrite = _grid.Columns[AclManagerGrantsHelper.ColWrite]!.Index;
        int iSpecial = _grid.Columns[AclManagerGrantsHelper.ColSpecial]!.Index;

        row.Cells[iIcon].Value = iconProvider.GetIcon(entry.Path);
        row.Cells[iPath].Value = entry.Path;
        row.Cells[iMode].Value = effectiveIsDeny ? AclManagerGrantsHelper.ModeDeny : AclManagerGrantsHelper.ModeAllow;
        row.Cells[iMode].ReadOnly = status == PathAclStatus.Unavailable;

        PopulateRightsCells(row, effectiveIsDeny, state, isPendingChange, iRead, iExecute, iWrite, iSpecial, savedRights: entry.SavedRights);

        if (!_isContainer && _grid.Columns.Contains(AclManagerGrantsHelper.ColOwner))
        {
            row.Cells[_grid.Columns[AclManagerGrantsHelper.ColOwner]!.Index].Value = effectiveIsDeny
                ? state.IsAdminOwner ? RightCheckState.Checked : RightCheckState.Unchecked
                : state.IsAccountOwner;
        }

        ApplyRowStatus(row, status, isPendingChange);
        _grid.Rows.Add(row);
    }

    public void RefreshRow(DataGridViewRow row, GrantedPathEntry entry)
    {
        bool isPending = _pending.IsPendingGrantChange(entry.Path, entry.IsDeny);
        RefreshRow(row, entry, isPending);
    }

    public void RefreshRow(DataGridViewRow row, GrantedPathEntry entry, bool isPendingChange,
        Action<bool>? setSuppressed = null)
    {
        setSuppressed?.Invoke(true);
        try
        {
            // For pending-modification rows, use effective IsDeny/Rights from the pending mod rather
            // than the (unmodified) DB entry, so the display reflects what will be applied.
            bool effectiveIsDeny = _pending.GetEffectiveIsDeny(entry);
            SavedRightsState? effectiveRights = _pending.GetEffectiveRights(entry);

            GrantRightsState state;
            PathAclStatus status;

            if (isPendingChange)
            {
                state = BuildStateFromSavedRights(effectiveIsDeny, effectiveRights);
                status = PathAclStatus.Available;
            }
            else
            {
                state = ReadRightsForEntry(entry);
                status = DetermineStatus(entry, state);
            }

            if (effectiveIsDeny)
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
                row.Cells[AclManagerGrantsHelper.ColRead].Value = RightCheckState.Checked;
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

            if (!_isContainer && _grid.Columns.Contains(AclManagerGrantsHelper.ColOwner))
                row.Cells[AclManagerGrantsHelper.ColOwner].Value = effectiveIsDeny
                    ? state.IsAdminOwner ? RightCheckState.Checked : RightCheckState.Unchecked
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
        bool isPending = _pending.IsPendingGrantChange(entry.Path, entry.IsDeny);

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
        if (!_pending.PendingAdds.ContainsKey(key))
        {
            bool wasIsDeny = _pending.PendingModifications.TryGetValue(key, out var existingMod)
                ? existingMod.WasIsDeny
                : entry.IsDeny;
            bool newIsDeny = existingMod?.NewIsDeny ?? entry.IsDeny;
            bool wasOwn = existingMod?.WasOwn ?? entry.SavedRights?.Own == true;
            // NewRights: preserve existing pending rights if any, otherwise use the DB entry's saved rights.
            var newRights = existingMod?.NewRights ?? entry.SavedRights;
            _pending.PendingModifications[key] = new PendingModification(
                entry, WasIsDeny: wasIsDeny, WasOwn: wasOwn,
                NewIsDeny: newIsDeny, NewRights: newRights);
        }

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
        var rawStatus = pathGrantService.CheckGrantStatus(entry.Path, _sid, entry.IsDeny);
        if (rawStatus == PathAclStatus.Unavailable)
            return PathAclStatus.Unavailable;

        bool isFolder = Directory.Exists(entry.Path);
        if (!_comparer.MatchesSavedRights(entry, state, _isContainer, isFolder))
            return PathAclStatus.Broken;

        return PathAclStatus.Available;
    }

    private GrantRightsState ReadRightsForEntry(GrantedPathEntry entry)
    {
        try
        {
            return pathGrantService.ReadGrantState(entry.Path, _sid, _groupSids);
        }
        catch (Exception ex)
        {
            log.Warn($"Failed to read rights for '{entry.Path}': {ex.Message}");
            return new GrantRightsState(
                RightCheckState.Unchecked, RightCheckState.Unchecked, RightCheckState.Unchecked,
                RightCheckState.Unchecked, RightCheckState.Unchecked, RightCheckState.Unchecked, RightCheckState.Unchecked,
                RightCheckState.Unchecked, false, 0, 0);
        }
    }

    /// <summary>
    /// Returns null when the path is inaccessible, so callers can skip auto-population.
    /// </summary>
    public GrantRightsState? TryReadRightsForEntry(GrantedPathEntry entry)
    {
        try
        {
            return pathGrantService.ReadGrantState(entry.Path, _sid, _groupSids);
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
    /// Returns the display <see cref="RightCheckState"/> for a rights checkbox cell.
    /// </summary>
    private static RightCheckState GetDisplayCheckState(bool? savedValue, RightCheckState ntfsState)
    {
        if (savedValue == null)
            return ntfsState;
        if (!savedValue.Value && ntfsState == RightCheckState.Indeterminate)
            return RightCheckState.Indeterminate;
        return savedValue.Value ? RightCheckState.Checked : RightCheckState.Unchecked;
    }

    /// <summary>
    /// Builds a synthetic <see cref="GrantRightsState"/> from explicit <paramref name="isDeny"/> and
    /// <paramref name="savedRights"/> so that pending entries can be displayed without an NTFS read.
    /// Uses the effective (pending) values rather than the unmodified DB entry fields.
    /// </summary>
    private static GrantRightsState BuildStateFromSavedRights(bool isDeny, SavedRightsState? savedRights)
    {
        var s = savedRights;
        if (s == null)
            return new GrantRightsState(
                RightCheckState.Unchecked, RightCheckState.Unchecked, RightCheckState.Unchecked,
                RightCheckState.Unchecked, RightCheckState.Unchecked, RightCheckState.Unchecked, RightCheckState.Unchecked,
                RightCheckState.Unchecked, false, 1, 0);

        if (!isDeny)
        {
            return new GrantRightsState(
                AllowExecute: s.Execute ? RightCheckState.Checked : RightCheckState.Unchecked,
                AllowWrite: s.Write ? RightCheckState.Checked : RightCheckState.Unchecked,
                AllowSpecial: s.Special ? RightCheckState.Checked : RightCheckState.Unchecked,
                DenyRead: RightCheckState.Unchecked,
                DenyExecute: RightCheckState.Unchecked,
                DenyWrite: RightCheckState.Unchecked,
                DenySpecial: RightCheckState.Unchecked,
                IsAccountOwner: s.Own ? RightCheckState.Checked : RightCheckState.Unchecked,
                IsAdminOwner: false,
                DirectAllowAceCount: 1,
                DirectDenyAceCount: 0);
        }

        return new GrantRightsState(
            AllowExecute: RightCheckState.Unchecked,
            AllowWrite: RightCheckState.Unchecked,
            AllowSpecial: RightCheckState.Unchecked,
            DenyRead: s.Read ? RightCheckState.Checked : RightCheckState.Unchecked,
            DenyExecute: s.Execute ? RightCheckState.Checked : RightCheckState.Unchecked,
            DenyWrite: RightCheckState.Checked,
            DenySpecial: RightCheckState.Checked,
            IsAccountOwner: RightCheckState.Unchecked,
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
                    if (cell.ColumnIndex != _grid.Columns[AclManagerGrantsHelper.ColPath]?.Index)
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

    private void PopulateRightsCells(DataGridViewRow row, bool effectiveIsDeny,
        GrantRightsState state, bool isPendingChange,
        int iRead, int iExecute, int iWrite, int iSpecial,
        SavedRightsState? savedRights = null)
    {
        if (effectiveIsDeny)
        {
            row.Cells[iRead].Value = isPendingChange
                ? state.DenyRead
                : GetDisplayCheckState(savedRights?.Read, state.DenyRead);
            row.Cells[iExecute].Value = isPendingChange
                ? state.DenyExecute
                : GetDisplayCheckState(savedRights?.Execute, state.DenyExecute);
            row.Cells[iWrite].Value = state.DenyWrite;
            row.Cells[iWrite].ReadOnly = true;
            row.Cells[iSpecial].Value = state.DenySpecial;
            row.Cells[iSpecial].ReadOnly = true;
        }
        else
        {
            row.Cells[iRead].Value = RightCheckState.Checked;
            row.Cells[iRead].ReadOnly = true;
            row.Cells[iExecute].Value = isPendingChange
                ? state.AllowExecute
                : GetDisplayCheckState(savedRights?.Execute, state.AllowExecute);
            row.Cells[iWrite].Value = isPendingChange
                ? state.AllowWrite
                : GetDisplayCheckState(savedRights?.Write, state.AllowWrite);
            row.Cells[iSpecial].Value = isPendingChange
                ? state.AllowSpecial
                : GetDisplayCheckState(savedRights?.Special, state.AllowSpecial);
        }
    }
}