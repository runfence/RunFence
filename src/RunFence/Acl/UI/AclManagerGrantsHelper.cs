using RunFence.Acl.UI.Forms;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Persistence;
using RunFence.UI;

namespace RunFence.Acl.UI;

/// <summary>
/// Encapsulates grants-grid population, rights display, and deferred ACE change tracking for
/// <see cref="AclManagerDialog"/>. Owns the suppressEvents flag and all direct interactions
/// with <see cref="IGrantedPathAclService"/>.
/// </summary>
public class AclManagerGrantsHelper(
    IGrantedPathAclService aclService,
    IAppConfigService appConfigService,
    ILoggingService log,
    IGrantConfigTracker grantConfigTracker,
    IDatabaseProvider databaseProvider,
    ISessionSaver sessionSaver,
    TraverseAutoManager traverseAutoManager)
{
    // Column name constants shared with AclManagerDialog and AclManagerTraverseHelper
    public const string ColIcon = "Icon";
    public const string ColPath = "Path";
    public const string ColMode = "Mode";
    public const string ColRead = "Read";
    public const string ColExecute = "Execute";
    public const string ColWrite = "Write";
    public const string ColSpecial = "Special";
    public const string ColOwner = "Owner";

    public const string ModeAllow = "Allow";
    public const string ModeDeny = "Deny";

    private readonly TraverseAutoManager _traverseAutoManager = traverseAutoManager;
    private DataGridView _grid = null!;
    private string _sid = null!;
    private bool _isContainer;
    private AclManagerPendingChanges _pending = null!;
    private GridSortHelper? _sortHelper;
    private AclManagerGrantRowRenderer _renderer = null!;

    public bool IsSuppressed { get; private set; }

    public HashSet<GrantedPathEntry> FixableEntries => _renderer.FixableEntries;

    public void Initialize(
        DataGridView grid, string sid, bool isContainer,
        IReadOnlyList<string> groupSids,
        AclManagerPendingChanges pending,
        GridSortHelper? sortHelper = null)
    {
        _grid = grid;
        _sid = sid;
        _isContainer = isContainer;
        _pending = pending;
        _sortHelper = sortHelper;

        _renderer = new AclManagerGrantRowRenderer(
            grid, sid, isContainer, aclService, log, groupSids, pending);
    }

    // --- Populate ---

    public void PopulateGrantsGrid()
    {
        IsSuppressed = true;
        _renderer.FixableEntries.Clear();
        try
        {
            _grid.Rows.Clear();
            bool hasLoadedConfigs = appConfigService.HasLoadedConfigs;

            List<GrantedPathEntry> grantEntries;
            var database = databaseProvider.GetDatabase();
            var dbGrants = database.GetAccount(_sid)?.Grants;
            if (dbGrants == null)
            {
                if (!hasLoadedConfigs && _pending.PendingAdds.Count == 0)
                    return;
                grantEntries = [];
            }
            else
            {
                grantEntries = dbGrants.Where(e => !e.IsTraverseOnly).ToList();
                if (grantEntries.Count == 0 && !hasLoadedConfigs && _pending.PendingAdds.Count == 0)
                    return;
            }

            // Auto-populate SavedRights for legacy entries (one-time migration).
            var populated = SavedRightsComparer.Instance.AutoPopulateMissingSavedRights(
                grantEntries, _renderer.TryReadRightsForEntry, _isContainer);
            if (populated.Count > 0)
                sessionSaver.SaveConfig();

            // All entries to display: DB entries + pending adds (pending adds not yet in DB).
            var allEntriesToShow = grantEntries.Concat(_pending.PendingAdds.Values).ToList();

            var mainEntries = allEntriesToShow
                .Where(e => GetEffectiveGrantConfigPath(e) == null)
                .ToList();
            AddGrantRows(mainEntries, "Main Config", configPath: null, showIfEmpty: hasLoadedConfigs);

            foreach (var configPath in appConfigService.GetLoadedConfigPaths())
            {
                var configEntries = allEntriesToShow
                    .Where(e => string.Equals(
                        GetEffectiveGrantConfigPath(e), configPath, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                AddGrantRows(configEntries, Path.GetFileName(configPath), configPath, showIfEmpty: true);
            }
        }
        finally
        {
            _grid.ClearSelection();
            IsSuppressed = false;
        }
    }

    /// <summary>
    /// Returns the effective config path for a grant entry, accounting for any pending
    /// config-section move that hasn't been applied yet.
    /// </summary>
    private string? GetEffectiveGrantConfigPath(GrantedPathEntry e)
    {
        var key = (Path.GetFullPath(e.Path), e.IsDeny);
        if (_pending.PendingConfigMoves.TryGetValue(key, out var pendingTarget))
            return pendingTarget;
        return grantConfigTracker.GetGrantConfigPath(_sid, e);
    }

    private void AddGrantRows(List<GrantedPathEntry> entries, string sectionTitle, string? configPath, bool showIfEmpty = false)
    {
        // Exclude entries pending removal or untrack from display.
        var visible = entries
            .Where(e => !_pending.IsPendingRemove(e.Path, e.IsDeny) && !_pending.IsUntrackGrant(e.Path, e.IsDeny))
            .ToList();

        if (!showIfEmpty && visible.Count == 0)
            return;

        // Sort within section when a column sort is active.
        if (_sortHelper?.IsSortActive == true)
            visible = _sortHelper.SortByActiveColumn(visible, e => e.Path).ToList();

        _grid.Rows.Add(AclManagerSectionHeader.CreateSectionHeaderRow(_grid, sectionTitle, configPath, titleCellIndex: 1));

        foreach (var entry in visible)
            _renderer.AddGrantRow(entry);
    }

    // --- Event delegate ---

    /// <summary>
    /// Called by the dialog's <c>OnGrantsCellValueChanged</c> for all columns except Own.
    /// Suppression check is inside; delegates to mode/rights change as appropriate.
    /// </summary>
    /// <returns>True when the traverse pending state changed and the traverse grid should be repopulated.</returns>
    public bool HandleCellValueChanged(DataGridViewRow row, string colName, GrantedPathEntry entry,
        Action<string> onError)
    {
        if (IsSuppressed)
            return false;

        if (colName == ColMode)
            return HandleModeChange(row, entry, onError);

        UpdateSavedRightsFromRow(entry, row);
        return false;
    }

    /// <returns>True when the traverse pending state changed.</returns>
    private bool HandleModeChange(DataGridViewRow row, GrantedPathEntry entry, Action<string> onError)
    {
        var newMode = row.Cells[ColMode].Value as string;
        bool newIsDeny = newMode == ModeDeny;
        if (newIsDeny == entry.IsDeny)
            return false;

        // Check if an entry with the new (target) mode already exists in DB or pending adds.
        var dbGrants = databaseProvider.GetDatabase().GetAccount(_sid)?.Grants;
        bool oppositeExists = _pending.IsPendingAdd(entry.Path, newIsDeny) ||
                              (dbGrants != null &&
                               dbGrants.Any(e => string.Equals(e.Path, entry.Path, StringComparison.OrdinalIgnoreCase) &&
                                                 e.IsDeny == newIsDeny && !e.IsTraverseOnly &&
                                                 !_pending.IsPendingRemove(e.Path, e.IsDeny)));
        if (oppositeExists)
        {
            // Revert the combo cell value — mode switch is not allowed.
            IsSuppressed = true;
            try
            {
                row.Cells[ColMode].Value = entry.IsDeny ? ModeDeny : ModeAllow;
            }
            finally
            {
                IsSuppressed = false;
            }

            onError("Cannot switch mode: an entry with the opposite mode already exists for this path.");
            return false;
        }

        bool wasAllow = !entry.IsDeny;
        bool wasIsDeny = entry.IsDeny;

        // Preserve Own from existing saved rights (mode switch does not change ownership).
        bool ownValue = entry.SavedRights?.Own ?? false;

        // Update entry mode and reset saved rights to mode defaults.
        entry.IsDeny = newIsDeny;
        entry.SavedRights = SavedRightsState.DefaultForMode(newIsDeny, own: ownValue);

        // Track in pending state. Must re-key if the entry was a pending add,
        // because the key includes isDeny which has now changed.
        var oldKey = (entry.Path, wasIsDeny);
        var newKey = (entry.Path, newIsDeny);
        _pending.PendingModifications.Remove(oldKey);
        if (_pending.PendingAdds.Remove(oldKey))
        {
            _pending.PendingAdds[newKey] = entry;
            if (_pending.PendingConfigMoves.Remove(oldKey, out var configTarget))
                _pending.PendingConfigMoves[newKey] = configTarget;
        }
        else
        {
            _pending.PendingModifications[newKey] = entry;
            if (_pending.PendingConfigMoves.Remove(oldKey, out var configTarget))
                _pending.PendingConfigMoves[newKey] = configTarget;
        }

        // Auto-manage traverse entries on mode switch.
        int traverseBefore = _pending.PendingTraverseAdds.Count + _pending.PendingTraverseRemoves.Count;
        var traversePath = TraverseAutoManager.GetTraversePath(entry.Path);
        if (traversePath != null)
        {
            if (wasAllow)
                _traverseAutoManager.AutoRemoveTraverseIfUnneeded(traversePath);
            else
                _traverseAutoManager.AutoAddTraverseIfMissing(traversePath);
        }

        int traverseAfter = _pending.PendingTraverseAdds.Count + _pending.PendingTraverseRemoves.Count;

        _renderer.RefreshRow(row, entry, isPendingChange: true, v => IsSuppressed = v);
        return traverseAfter != traverseBefore;
    }

    /// <summary>
    /// Updates <see cref="GrantedPathEntry.SavedRights"/> from the current checkbox values in the row,
    /// then marks the entry as a pending modification (or updates it in PendingAdds if already there).
    /// </summary>
    private void UpdateSavedRightsFromRow(GrantedPathEntry entry, DataGridViewRow row)
    {
        bool ownValue = false;
        if (!_isContainer && _grid.Columns.Contains(ColOwner))
            ownValue = GetCheck(row, ColOwner) == CheckState.Checked;

        if (entry.IsDeny)
        {
            entry.SavedRights = SavedRightsState.DefaultForMode(true, own: ownValue) with
            {
                Execute = GetCheck(row, ColExecute) == CheckState.Checked,
                Read = GetCheck(row, ColRead) == CheckState.Checked
            };
        }
        else
        {
            entry.SavedRights = SavedRightsState.DefaultForMode(false, own: ownValue) with
            {
                Execute = GetCheck(row, ColExecute) == CheckState.Checked,
                Write = GetCheck(row, ColWrite) == CheckState.Checked,
                Special = GetCheck(row, ColSpecial) == CheckState.Checked
            };
        }

        var key = (entry.Path, entry.IsDeny);
        if (_pending.PendingAdds.ContainsKey(key))
        {
            _pending.PendingAdds[key] = entry;
            _renderer.RefreshRowBackground(row, entry);
            return;
        }

        // DB entry: check if the change has been reverted (new SavedRights matches NTFS state).
        var ntfsState = _renderer.TryReadRightsForEntry(entry);
        if (ntfsState != null && SavedRightsComparer.Instance.MatchesSavedRights(entry, ntfsState, _isContainer))
        {
            // Effectively reverted — remove pending modification and restore row color.
            _pending.PendingModifications.Remove(key);
            _renderer.ReapplyRowStatus(row, entry, ntfsState);
            return;
        }

        _pending.PendingModifications[key] = entry;
        _renderer.RefreshRowBackground(row, entry);
    }
    
    public GrantRightsState ReadRightsForEntry(GrantedPathEntry entry)
        => _renderer.ReadRightsForEntry(entry);

    public GrantRightsState? TryReadRightsForEntry(GrantedPathEntry entry)
        => _renderer.TryReadRightsForEntry(entry);

    public void FixBrokenGrant(GrantedPathEntry entry, DataGridViewRow row)
        => _renderer.FixBrokenGrant(entry, row);

    private static CheckState GetCheck(DataGridViewRow row, string colName)
    {
        if (row.DataGridView == null || !row.DataGridView.Columns.Contains(colName))
            return CheckState.Unchecked;
        var val = row.Cells[colName].Value;
        return val is CheckState cs ? cs : CheckState.Unchecked;
    }
}