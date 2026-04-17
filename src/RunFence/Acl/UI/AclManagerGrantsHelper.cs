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
/// with <see cref="IPathGrantService"/> via <see cref="AclManagerGrantRowRenderer"/>.
/// </summary>
public class AclManagerGrantsHelper(
    IAppConfigService appConfigService,
    IGrantConfigTracker grantConfigTracker,
    IDatabaseProvider databaseProvider,
    ISessionSaver sessionSaver,
    TraverseAutoManager traverseAutoManager,
    Func<AclManagerGrantRowRenderer> rendererFactory)
{
    private readonly AclManagerGrantRowRenderer _renderer = rendererFactory();
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

        _renderer.Initialize(grid, sid, isContainer, groupSids, pending);
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
                .Where(e => _pending.GetEffectiveConfigPath(e, grantConfigTracker, _sid) == null)
                .ToList();
            AddGrantRows(mainEntries, "Main Config", configPath: null, showIfEmpty: hasLoadedConfigs);

            foreach (var configPath in appConfigService.GetLoadedConfigPaths())
            {
                var configEntries = allEntriesToShow
                    .Where(e => string.Equals(
                        _pending.GetEffectiveConfigPath(e, grantConfigTracker, _sid), configPath, StringComparison.OrdinalIgnoreCase))
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

        // Use the effective current mode (from pending mod if a prior switch exists) to detect no-ops.
        bool effectiveIsDeny = _pending.GetEffectiveIsDeny(entry);
        if (newIsDeny == effectiveIsDeny)
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
                row.Cells[ColMode].Value = effectiveIsDeny ? ModeDeny : ModeAllow;
            }
            finally
            {
                IsSuppressed = false;
            }

            onError("Cannot switch mode: an entry with the opposite mode already exists for this path.");
            return false;
        }

        bool wasAllow = !effectiveIsDeny;

        // Preserve Own from existing pending rights or original DB rights (mode switch does not change ownership).
        bool ownValue = _pending.GetEffectiveRights(entry)?.Own ?? false;

        var newSavedRights = SavedRightsState.DefaultForMode(newIsDeny, own: ownValue);

        // The original DB key is (entry.Path, entry.IsDeny) — entry.IsDeny is NOT mutated.
        var dbKey = (entry.Path, entry.IsDeny);

        // Track in pending state. Must re-key PendingAdds if the entry was a pending add,
        // because PendingAdds key includes isDeny which has conceptually changed.
        var newPendingAddKey = (entry.Path, newIsDeny);

        // Preserve the true original NTFS wasIsDeny across multiple mode switches: if there is
        // already a pending modification under dbKey, its WasIsDeny reflects the actual NTFS state.
        // This ensures double-switch (Allow→Deny→Allow) does not try to revert a non-existent ACE.
        bool originalWasIsDeny = _pending.PendingModifications.TryGetValue(dbKey, out var existingMod)
            ? existingMod.WasIsDeny
            : entry.IsDeny;

        if (_pending.PendingAdds.Remove(dbKey))
        {
            // Was a pending add — re-key the pending add entry to the new mode.
            entry.IsDeny = newIsDeny;
            entry.SavedRights = newSavedRights;
            _pending.PendingAdds[newPendingAddKey] = entry;
            if (_pending.PendingConfigMoves.Remove(dbKey, out var configTarget))
                _pending.PendingConfigMoves[newPendingAddKey] = configTarget;
        }
        else
        {
            // DB entry — store new mode and rights in PendingModification WITHOUT mutating entry.
            // originalWasIsDeny is the mode that exists on NTFS — used by the applier to revert
            // the correct ACE before applying the new mode's ACE. When originalWasIsDeny == newIsDeny
            // (double-switch back to original mode), the applier falls through to the rights-diff path.
            // ownValue is the Own value captured before the mode switch.
            _pending.PendingModifications[dbKey] = new PendingModification(
                entry, WasIsDeny: originalWasIsDeny, WasOwn: ownValue,
                NewIsDeny: newIsDeny, NewRights: newSavedRights);
            // Re-key the config move from wherever it currently lives to the new mode key.
            // On a first switch the move lives at dbKey; on a subsequent switch it already lives
            // at the previous effective mode key (existingMod.NewIsDeny), so check that key first.
            var prevEffectiveKey = existingMod != null ? (entry.Path, existingMod.NewIsDeny) : dbKey;
            bool hadConfigMove = _pending.PendingConfigMoves.Remove(prevEffectiveKey, out var configTarget);
            if (!hadConfigMove && prevEffectiveKey != dbKey)
                hadConfigMove = _pending.PendingConfigMoves.Remove(dbKey, out configTarget);
            if (hadConfigMove)
                _pending.PendingConfigMoves[newPendingAddKey] = configTarget;
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
    /// Computes new SavedRights from the current checkbox values in the row, then marks the entry
    /// as a pending modification (or updates it in PendingAdds if already there), without mutating
    /// the live DB entry.
    /// </summary>
    private void UpdateSavedRightsFromRow(GrantedPathEntry entry, DataGridViewRow row)
    {
        bool ownValue = false;
        if (!_isContainer && _grid.Columns.Contains(ColOwner))
            ownValue = GetCheck(row, ColOwner) == RightCheckState.Checked;

        // Capture original Own from the entry's NTFS-committed rights (before any pending change),
        // so WasOwn can reflect the true NTFS state. entry.SavedRights is never mutated.
        bool originalOwn = entry.SavedRights?.Own == true;

        // Use the effective IsDeny (from pending mode switch if any) to compute the correct rights.
        bool effectiveIsDeny = _pending.GetEffectiveIsDeny(entry);

        SavedRightsState newSavedRights;
        if (effectiveIsDeny)
        {
            newSavedRights = SavedRightsState.DefaultForMode(true, own: ownValue) with
            {
                Execute = GetCheck(row, ColExecute) == RightCheckState.Checked,
                Read = GetCheck(row, ColRead) == RightCheckState.Checked
            };
        }
        else
        {
            newSavedRights = SavedRightsState.DefaultForMode(false, own: ownValue) with
            {
                Execute = GetCheck(row, ColExecute) == RightCheckState.Checked,
                Write = GetCheck(row, ColWrite) == RightCheckState.Checked,
                Special = GetCheck(row, ColSpecial) == RightCheckState.Checked
            };
        }

        // PendingAdds are keyed by (path, effectiveIsDeny) for mode-switched entries.
        var addKey = (entry.Path, effectiveIsDeny);
        if (_pending.PendingAdds.ContainsKey(addKey))
        {
            // For pending adds the entry itself is the only record — update SavedRights directly.
            entry.SavedRights = newSavedRights;
            _pending.PendingAdds[addKey] = entry;
            _renderer.RefreshRowBackground(row, entry);
            return;
        }

        // DB entry: check if the change has been reverted (new rights match NTFS state for the effective mode).
        // Use a temporary entry to pass effective IsDeny + new rights to MatchesSavedRights without mutating.
        var ntfsState = _renderer.TryReadRightsForEntry(entry);
        bool isFolder = Directory.Exists(entry.Path);
        if (ntfsState != null)
        {
            var tempEntry = new GrantedPathEntry { Path = entry.Path, IsDeny = effectiveIsDeny, SavedRights = newSavedRights };
            if (SavedRightsComparer.Instance.MatchesSavedRights(tempEntry, ntfsState, _isContainer, isFolder))
            {
                // Effectively reverted — remove pending modification and restore row color.
                _pending.PendingModifications.Remove((entry.Path, entry.IsDeny));
                _renderer.ReapplyRowStatus(row, entry, ntfsState);
                return;
            }
        }

        // Store new rights in PendingModification without mutating entry.SavedRights.
        // Preserve WasIsDeny and WasOwn from any existing modification, which reflect the true NTFS state.
        var dbKey = (entry.Path, entry.IsDeny);
        bool wasIsDeny = _pending.PendingModifications.TryGetValue(dbKey, out var existingMod)
            ? existingMod.WasIsDeny
            : entry.IsDeny;
        bool newIsDeny = existingMod?.NewIsDeny ?? effectiveIsDeny;
        bool wasOwn = existingMod?.WasOwn ?? originalOwn;
        _pending.PendingModifications[dbKey] = new PendingModification(
            entry, WasIsDeny: wasIsDeny, WasOwn: wasOwn,
            NewIsDeny: newIsDeny, NewRights: newSavedRights);
        _renderer.RefreshRowBackground(row, entry);
    }

    public GrantRightsState? TryReadRightsForEntry(GrantedPathEntry entry)
        => _renderer.TryReadRightsForEntry(entry);

    public void FixBrokenGrant(GrantedPathEntry entry, DataGridViewRow row)
        => _renderer.FixBrokenGrant(entry, row);

    private static RightCheckState GetCheck(DataGridViewRow row, string colName)
    {
        if (row.DataGridView == null || !row.DataGridView.Columns.Contains(colName))
            return RightCheckState.Unchecked;
        var val = row.Cells[colName].Value;
        return val is RightCheckState cs ? cs : RightCheckState.Unchecked;
    }
}