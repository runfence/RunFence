using RunFence.Acl.UI.Forms;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Persistence;

namespace RunFence.Acl.UI;

/// <summary>
/// Handles user-initiated actions in <see cref="AclManagerDialog"/>: Fix ACLs, shell drag-drop,
/// Open in Explorer, path selection, direct grant-path addition, deferred grant removal,
/// and Own checkbox changes.
/// </summary>
public class AclManagerActionOrchestrator(
    IDatabaseProvider databaseProvider,
    AclManagerGrantsHelper grantsHelper,
    AclManagerTraverseHelper traverseHelper,
    TraverseAutoManager traverseAutoManager,
    IReparsePointPromptHelper reparsePointHelper,
    ShellHelper shellHelper)
{
    private readonly AclManagerGrantsHelper _grantsHelper = grantsHelper;
    private readonly AclManagerTraverseHelper _traverseHelper = traverseHelper;
    private readonly TraverseAutoManager _traverseAutoManager = traverseAutoManager;
    private string _sid = null!;
    private bool _isContainer;
    private IWin32Window _owner = null!;
    private AclManagerPendingChanges _pending = null!;
    private IAclManagerGridRefresher _gridRefresher = null!;

    public void Initialize(
        string sid,
        bool isContainer,
        IWin32Window owner,
        AclManagerPendingChanges pending,
        IAclManagerGridRefresher gridRefresher)
    {
        _sid = sid;
        _isContainer = isContainer;
        _owner = owner;
        _pending = pending;
        _gridRefresher = gridRefresher;
    }

    public void FixAcls(List<DataGridViewRow> expandedRows, bool isTraverseTab)
    {
        if (isTraverseTab)
        {
            var entries = expandedRows
                .Where(r => r.Tag is GrantedPathEntry te && _traverseHelper.FixableEntries.Contains(te))
                .Select(r => (GrantedPathEntry)r.Tag!)
                .ToList();
            if (entries.Count > 0)
                _traverseHelper.FixTraversePaths(entries);
            return;
        }

        var fixableRows = expandedRows
            .Where(r => r.Tag is GrantedPathEntry e && _grantsHelper.FixableEntries.Contains(e))
            .ToList();
        foreach (var fixRow in fixableRows)
            _grantsHelper.FixBrokenGrant((GrantedPathEntry)fixRow.Tag!, fixRow);
    }

    /// <param name="targetConfigPath">Config path of the section the user dropped onto; null for main config.</param>
    /// <returns>First duplicate-path error encountered, or null if all paths added successfully.</returns>
    public string? HandleShellDropOnGrants(string[] paths, string? targetConfigPath = null)
    {
        var regularPaths = paths.Where(p => !reparsePointHelper.IsReparsePoint(p)).ToList();
        var reparsePaths = paths.Where(p => reparsePointHelper.IsReparsePoint(p)).ToList();

        string? firstError = null;

        if (regularPaths.Count > 0)
        {
            bool? isDeny = PromptGrantMode(_owner);
            if (isDeny != null)
                foreach (var path in regularPaths)
                    firstError = AddGrantPathDirect(path, isDeny.Value, targetConfigPath) ?? firstError;
        }

        foreach (var path in reparsePaths)
        {
            // By design: reparse-point targets are always added as Allow (deny on symlink targets is unreliable)
            var pathsToAdd = reparsePointHelper.ResolveForAdd(path, _owner);
            firstError = pathsToAdd.Aggregate(firstError, (current, p) => AddGrantPathDirect(p, isDeny: false, targetConfigPath) ?? current);
        }

        return firstError;
    }

    /// <summary>Returns false (Allow), true (Deny), or null (Cancel).</summary>
    private static bool? PromptGrantMode(IWin32Window owner)
    {
        var allowButton = new TaskDialogButton("Add Allow");
        var denyButton = new TaskDialogButton("Add Deny");
        var cancelButton = new TaskDialogButton("Cancel");

        var page = new TaskDialogPage
        {
            Caption = "Add Grant",
            Heading = "How would you like to add the dropped path(s)?",
            Buttons = { allowButton, denyButton, cancelButton },
            DefaultButton = allowButton
        };

        var result = TaskDialog.ShowDialog(owner, page);
        if (result == allowButton)
            return false;
        if (result == denyButton)
            return true;
        return null;
    }

    public void HandleShellDropOnTraverse(string[] paths)
    {
        foreach (var path in paths)
        {
            var pathsToAdd = reparsePointHelper.ResolveForAdd(path, _owner);
            foreach (var p in pathsToAdd)
                _traverseHelper.AddTraversePathDirect(p);
        }
    }

    public void OpenInExplorer(string path)
    {
        string folder = Directory.Exists(path) ? path : Path.GetDirectoryName(path) ?? path;
        shellHelper.OpenInExplorer(folder);
    }

    /// <summary>
    /// Queues a grant entry for DB-only removal (no NTFS ACE removal). If the entry was only in
    /// pending adds (not yet in DB), discards it. Otherwise adds to PendingUntrackGrants so the
    /// DB entry is removed on Apply while the NTFS ACE is left intact.
    /// </summary>
    public void UntrackGrantPath(GrantedPathEntry entry)
    {
        var key = (entry.Path, entry.IsDeny);
        if (CancelPendingAddOrReturn(key))
            return;

        _pending.PendingUntrackGrants[key] = entry;
        // Discard any pending modification or config move — untrack supersedes them.
        // Also discard any config move re-keyed to the new mode by a prior mode switch.
        if (_pending.PendingModifications.Remove(key, out var mod))
            _pending.PendingConfigMoves.Remove((entry.Path, mod.NewIsDeny));
        _pending.PendingConfigMoves.Remove(key);
    }

    /// <summary>
    /// Queues a traverse entry for DB-only removal (no NTFS ACE removal). If the entry was only in
    /// pending adds, discards it. Otherwise adds to PendingUntrackTraverse so the DB entry is
    /// removed on Apply while the NTFS ACE is left intact.
    /// </summary>
    public void UntrackTraversePath(GrantedPathEntry entry)
    {
        var normalized = Path.GetFullPath(entry.Path);

        // Pending add — discard entirely.
        if (_pending.PendingTraverseAdds.Remove(normalized))
        {
            _pending.PendingTraverseConfigMoves.Remove(normalized);
            return;
        }

        _pending.PendingUntrackTraverse[normalized] = entry;
        // Discard any pending fix or config move — untrack supersedes them.
        _pending.PendingTraverseFixes.Remove(normalized);
        _pending.PendingTraverseConfigMoves.Remove(normalized);
    }

    /// <summary>
    /// Defers removal of a grant entry. If the entry was only in pending (not yet in DB),
    /// discards it. Otherwise queues it in PendingRemoves and auto-removes traverse if unneeded.
    /// </summary>
    public void RemoveGrantPathDeferred(GrantedPathEntry entry)
    {
        var key = (entry.Path, entry.IsDeny);
        if (CancelPendingAddOrReturn(key))
            return;

        // Queue the existing DB entry for deferred removal.
        _pending.PendingRemoves[key] = entry;
        // Discard any pending modification or config move — removal supersedes them.
        // Also discard any config move re-keyed to the new mode by a prior mode switch.
        if (_pending.PendingModifications.Remove(key, out var mod))
            _pending.PendingConfigMoves.Remove((entry.Path, mod.NewIsDeny));
        _pending.PendingConfigMoves.Remove(key);

        // Auto-remove traverse if no other allow grants depend on it.
        if (!entry.IsDeny)
        {
            var traversePath = TraverseAutoManager.GetTraversePath(entry.Path);
            if (traversePath != null)
                _traverseAutoManager.AutoRemoveTraverseIfUnneeded(traversePath);
        }
    }

    /// <summary>
    /// If the entry was a pending add, discards it (removes from <see cref="AclManagerPendingChanges.PendingAdds"/>
    /// and <see cref="AclManagerPendingChanges.PendingConfigMoves"/>, auto-removes traverse when allow)
    /// and returns true. Returns false when no pending add was found, indicating the caller should
    /// proceed with its DB-entry path.
    /// </summary>
    private bool CancelPendingAddOrReturn((string Path, bool IsDeny) key)
    {
        if (!_pending.PendingAdds.Remove(key))
            return false;

        _pending.PendingConfigMoves.Remove(key);
        if (!key.IsDeny)
        {
            var traversePath = TraverseAutoManager.GetTraversePath(key.Path);
            if (traversePath != null)
                _traverseAutoManager.AutoRemoveTraverseIfUnneeded(traversePath);
        }

        return true;
    }

    /// <summary>
    /// Handles the Own checkbox change for a grant row. Records new ownership in PendingModification
    /// without mutating the live DB entry, marks the row as pending, and refreshes buttons.
    /// The <paramref name="updateActionButtons"/> callback is called after state changes.
    /// </summary>
    public void HandleOwnChange(DataGridViewRow row, GrantedPathEntry entry, Action updateActionButtons)
    {
        // Ownership change is deferred: record in PendingModification without mutating entry.SavedRights.
        // Deny+unchecked → no dialog, no NTFS write until Apply (no-op on Apply per plan).
        // The actual NTFS ownership change happens in AclManagerApplyOrchestrator.ApplyAsync.
        var checkState = (RightCheckState)(row.Cells[AclManagerGrantsHelper.ColOwner].Value ?? RightCheckState.Unchecked);
        bool ownValue = checkState == RightCheckState.Checked;

        // Capture original Own from the entry's NTFS-committed rights (before any pending change).
        bool originalOwn = entry.SavedRights?.Own == true;

        var dbKey = (entry.Path, entry.IsDeny);
        var addKey = (entry.Path, _pending.GetEffectiveIsDeny(entry));
        if (_pending.PendingAdds.ContainsKey(addKey))
        {
            // For pending adds the entry itself is the only record — update SavedRights directly.
            entry.SavedRights = entry.SavedRights != null
                ? entry.SavedRights with { Own = ownValue }
                : SavedRightsState.DefaultForMode(entry.IsDeny, own: ownValue);
        }
        else
        {
            // DB entry: store updated rights in PendingModification without mutating entry.SavedRights.
            // Preserve WasIsDeny from any existing modification — it reflects the true NTFS mode and must
            // not be overwritten when only ownership (not mode) changes after a prior mode switch.
            // Preserve WasOwn from existing mod if already tracked; otherwise use originalOwn.
            bool wasIsDeny = _pending.PendingModifications.TryGetValue(dbKey, out var existingMod)
                ? existingMod.WasIsDeny
                : entry.IsDeny;
            bool newIsDeny = existingMod?.NewIsDeny ?? entry.IsDeny;
            bool wasOwn = existingMod?.WasOwn ?? originalOwn;

            // Compute new rights by updating Own on the current effective rights.
            var effectiveRights = _pending.GetEffectiveRights(entry) ?? SavedRightsState.DefaultForMode(entry.IsDeny);
            var newSavedRights = effectiveRights with { Own = ownValue };

            _pending.PendingModifications[dbKey] = new PendingModification(
                entry, WasIsDeny: wasIsDeny, WasOwn: wasOwn,
                NewIsDeny: newIsDeny, NewRights: newSavedRights);
        }

        AclManagerGrantRowRenderer.SetPendingRowColor(row);
        _grantsHelper.FixableEntries.Remove(entry);
        updateActionButtons();
    }

    /// <summary>
    /// Adds a grant path entry in deferred mode. Creates a <see cref="GrantedPathEntry"/> with
    /// mode-default saved rights (pre-populated from NTFS when ACEs exist) and adds it to
    /// <see cref="AclManagerPendingChanges.PendingAdds"/>. When the opposite mode already exists
    /// for the same path, <paramref name="isDeny"/> is flipped to match. For allow grants,
    /// auto-adds a traverse entry for the grant folder itself (folder grant) or its parent directory (file grant), also deferred.
    /// No NTFS or DB writes occur until Apply.
    /// Returns an error message string when the path is already in the list; null on success.
    /// </summary>
    /// <param name="targetConfigPath">When non-null, records a pending config-section assignment for the new entry.</param>
    public string? AddGrantPathDirect(string selectedPath, bool isDeny, string? targetConfigPath = null)
    {
        if (string.IsNullOrEmpty(selectedPath))
            return null;

        var normalized = Path.GetFullPath(selectedPath);

        var database = databaseProvider.GetDatabase();

        // 1. Same-mode duplicate check (pending adds + DB, not pending removal).
        if (_pending.ExistsInDbOrPending(database, _sid, normalized, isDeny))
            return "This path is already in the list.";

        // 2. Opposite-mode check: if opposite mode exists, flip isDeny to match.
        if (_pending.ExistsInDbOrPending(database, _sid, normalized, !isDeny))
        {
            isDeny = !isDeny;
            // After flipping, re-check for same-mode duplicate with the new mode.
            if (_pending.ExistsInDbOrPending(database, _sid, normalized, isDeny))
                return "This path is already in the list.";
        }

        // 3. If this path is queued for removal or untrack (same final mode), cancel it.
        bool cancelledRemoval = _pending.PendingRemoves.Remove((normalized, isDeny));
        bool cancelledUntrack = !cancelledRemoval && _pending.PendingUntrackGrants.Remove((normalized, isDeny));
        if (cancelledRemoval || cancelledUntrack)
        {
            bool traverseRestored = false;
            if (!isDeny)
            {
                var traversePath = TraverseAutoManager.GetTraversePath(normalized);
                if (traversePath != null)
                    traverseRestored = _traverseHelper.TryEnsureTraverseAccess(traversePath);
            }

            _gridRefresher.RefreshGrantsGrid();
            if (traverseRestored)
                _gridRefresher.RefreshTraverseGrid();
            return null;
        }

        // 4. Create the entry with mode-default saved rights.
        // Allow mode: Read always on, Execute/Write/Special/Own off by default.
        // Deny mode: Write+Special always on, Execute/Read/Own off by default.
        var savedRights = SavedRightsState.DefaultForMode(isDeny);

        var entry = new GrantedPathEntry
        {
            Path = normalized,
            IsDeny = isDeny,
            SavedRights = savedRights
        };

        // 5. Pre-populate SavedRights from existing NTFS ACEs when the matching mode has direct ACEs.
        var ntfsState = _grantsHelper.TryReadRightsForEntry(entry);
        if (ntfsState != null)
        {
            int aceCount = isDeny ? ntfsState.DirectDenyAceCount : ntfsState.DirectAllowAceCount;
            if (aceCount > 0)
            {
                bool isFolder = Directory.Exists(normalized);
                entry.SavedRights = SavedRightsComparer.FromNtfsState(ntfsState, isDeny, _isContainer, isFolder);
            }
        }

        _pending.PendingAdds[(normalized, isDeny)] = entry;

        // Record target config if the user dropped onto a specific config section.
        if (targetConfigPath != null)
            _pending.PendingConfigMoves[(normalized, isDeny)] = targetConfigPath;

        // 6. For allow grants: auto-add traverse entry for the grant path (folder grant) or its parent (file grant).
        bool traverseAdded = false;
        if (!isDeny)
        {
            var traversePath = TraverseAutoManager.GetTraversePath(normalized);
            if (traversePath != null)
                traverseAdded = _traverseHelper.TryEnsureTraverseAccess(traversePath);
        }

        _gridRefresher.RefreshGrantsGrid();
        if (traverseAdded)
            _gridRefresher.RefreshTraverseGrid();
        return null;
    }
}

/// <summary>
/// Provides grid refresh callbacks from <see cref="AclManagerDialog"/> to
/// <see cref="AclManagerActionOrchestrator"/>, ensuring sort glyphs are preserved after repopulation.
/// </summary>
public interface IAclManagerGridRefresher
{
    void RefreshGrantsGrid();
    void RefreshTraverseGrid();
}