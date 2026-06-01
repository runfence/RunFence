using RunFence.Acl.UI.Forms;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Persistence;

namespace RunFence.Acl.UI;

/// <summary>
/// Handles user-initiated actions in <see cref="AclManagerDialog"/>: Fix ACLs, shell drag-drop,
/// path selection, direct grant-path addition, and deferred grant/traverse removal.
/// </summary>
public class AclManagerActionOrchestrator(
    IDatabaseProvider databaseProvider,
    AclManagerGrantsHelper grantsHelper,
    AclManagerTraverseHelper traverseHelper,
    AclManagerTraverseOperations traverseOperations,
    TraverseAutoManager traverseAutoManager,
    IReparsePointPromptHelper reparsePointHelper,
    ISpecificContainerAceConflictDetector specificContainerAceConflictDetector)
{
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
                .Where(r => r.Tag is GrantedPathEntry te && traverseHelper.FixableEntries.Contains(te))
                .Select(r => (GrantedPathEntry)r.Tag!)
                .ToList();
            if (entries.Count > 0)
                traverseOperations.FixTraversePaths(entries);
            return;
        }

        var fixableRows = expandedRows
            .Where(r => r.Tag is GrantedPathEntry e && grantsHelper.FixableEntries.Contains(e))
            .ToList();
        foreach (var fixRow in fixableRows)
            grantsHelper.FixBrokenGrant((GrantedPathEntry)fixRow.Tag!, fixRow);
    }

    /// <param name="targetConfigPath">Config path of the section the user dropped onto; null for main config.</param>
    /// <returns>First duplicate-path error encountered, or null if all paths added successfully.</returns>
    public string? HandleShellDropOnGrants(
        string[] paths,
        string? targetConfigPath = null,
        bool hasExplicitTargetSection = false)
    {
        var regularPaths = paths.Where(p => !reparsePointHelper.IsReparsePoint(p)).ToList();
        var reparsePaths = paths.Where(p => reparsePointHelper.IsReparsePoint(p)).ToList();

        string? firstError = null;

        if (regularPaths.Count > 0)
        {
            bool? isDeny = PromptGrantMode(_owner);
            if (isDeny != null)
                foreach (var path in regularPaths)
                    firstError = AddGrantPathDirect(path, isDeny.Value, targetConfigPath, hasExplicitTargetSection) ?? firstError;
        }

        foreach (var path in reparsePaths)
        {
            // By design: reparse-point targets are always added as Allow (deny on symlink targets is unreliable)
            var pathsToAdd = reparsePointHelper.ResolveForAdd(path, _owner);
            firstError = pathsToAdd.Aggregate(firstError, (current, p) =>
                AddGrantPathDirect(p, isDeny: false, targetConfigPath, hasExplicitTargetSection) ?? current);
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
            DefaultButton = allowButton,
            AllowCancel = true
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
                traverseOperations.AddTraversePathDirect(p);
        }
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

        _pending.Grants.UntrackGrant(entry);
        // Discard any pending modification or config move — untrack supersedes them.
        // Also discard any config move re-keyed to the new mode by a prior mode switch.
        _pending.Grants.RemoveGrantFix(entry.Path, entry.IsDeny);
        if (_pending.Grants.RemoveGrantModification(entry.Path, entry.IsDeny, out var mod))
            _pending.Grants.RemoveGrantConfigMove(entry.Path, mod!.NewIsDeny, out _);
        _pending.Grants.RemoveGrantConfigMove(entry.Path, entry.IsDeny, out _);
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
        if (_pending.Traverse.RemoveTraverse(normalized))
        {
            _pending.Traverse.RemoveTraverseConfigMove(normalized, out _);
            return;
        }

        _pending.Traverse.UntrackTraverse(entry);
        // Discard any pending fix or config move — untrack supersedes them.
        _pending.Traverse.RemoveTraverseFix(normalized);
        _pending.Traverse.RemoveTraverseConfigMove(normalized, out _);
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
        _pending.Grants.MarkGrantForRemoval(entry);
        // Discard any pending modification or config move — removal supersedes them.
        // Also discard any config move re-keyed to the new mode by a prior mode switch.
        _pending.Grants.RemoveGrantFix(entry.Path, entry.IsDeny);
        if (_pending.Grants.RemoveGrantModification(entry.Path, entry.IsDeny, out var mod))
            _pending.Grants.RemoveGrantConfigMove(entry.Path, mod!.NewIsDeny, out _);
        _pending.Grants.RemoveGrantConfigMove(entry.Path, entry.IsDeny, out _);

        // Auto-remove traverse if no other allow grants depend on it.
        if (!entry.IsDeny)
        {
            var traversePath = traverseAutoManager.GetTraversePath(entry.Path);
            if (traversePath != null)
                traverseAutoManager.AutoRemoveTraverseIfUnneeded(traversePath);
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
        if (!_pending.Grants.RemoveGrant(key.Path, key.IsDeny))
            return false;

        _pending.Grants.RemoveGrantConfigMove(key.Path, key.IsDeny, out _);
        if (!key.IsDeny)
        {
            var traversePath = traverseAutoManager.GetTraversePath(key.Path);
            if (traversePath != null)
                traverseAutoManager.AutoRemoveTraverseIfUnneeded(traversePath);
        }

        return true;
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
    /// <param name="targetConfigPath">Target config section for an explicit section-targeted add; null represents Main Config when <paramref name="hasExplicitTargetSection"/> is true.</param>
    /// <param name="hasExplicitTargetSection">True when the caller explicitly targeted a config section, including Main Config.</param>
    public string? AddGrantPathDirect(
        string selectedPath,
        bool isDeny,
        string? targetConfigPath = null,
        bool hasExplicitTargetSection = false)
    {
        if (string.IsNullOrEmpty(selectedPath))
            return null;

        var normalized = Path.GetFullPath(selectedPath);

        var database = databaseProvider.GetDatabase();

        // 1. Same-mode duplicate check. A committed DB entry is a duplicate only when its
        // explicit ACE is already healthy; otherwise adding the path again means "fix it".
        if (_pending.Grants.IsPendingAdd(normalized, isDeny))
            return "This path is already in the list.";
        if (TryQueueExistingGrantFix(database, normalized, isDeny, targetConfigPath, hasExplicitTargetSection))
            return null;
        if (ExistsCommittedGrant(database, normalized, isDeny))
            return "This path is already in the list.";

        var conflictMessage = AclConflictWarningHelper.GetConflictMessage(_sid, normalized, isDeny, specificContainerAceConflictDetector);
        if (conflictMessage != null)
            return conflictMessage;

        // 2. Opposite-mode check: if opposite mode exists, flip isDeny to match.
        if (_pending.Grants.IsPendingAdd(normalized, !isDeny) ||
            ExistsCommittedGrant(database, normalized, !isDeny))
        {
            isDeny = !isDeny;
            // After flipping, re-check for same-mode duplicate with the new mode.
            if (_pending.Grants.IsPendingAdd(normalized, isDeny))
                return "This path is already in the list.";
            if (TryQueueExistingGrantFix(database, normalized, isDeny, targetConfigPath, hasExplicitTargetSection))
                return null;
            if (ExistsCommittedGrant(database, normalized, isDeny))
                return "This path is already in the list.";
            conflictMessage = AclConflictWarningHelper.GetConflictMessage(_sid, normalized, isDeny, specificContainerAceConflictDetector);
            if (conflictMessage != null)
                return conflictMessage;
        }

        // 3. If this path is queued for removal or untrack (same final mode), cancel it.
        bool cancelledRemoval = _pending.Grants.CancelGrantRemoval(normalized, isDeny);
        bool cancelledUntrack = !cancelledRemoval && _pending.Grants.RemoveUntrackedGrant(normalized, isDeny);
        if (cancelledRemoval || cancelledUntrack)
        {
            var existingEntry = FindCommittedGrant(database, normalized, isDeny);
            if (hasExplicitTargetSection &&
                existingEntry != null &&
                grantsHelper.TargetsDifferentConfigSection(existingEntry, targetConfigPath))
            {
                _pending.Grants.MoveGrantConfig(existingEntry, targetConfigPath);
            }

            bool traverseRestored = false;
            if (!isDeny)
            {
                var traversePath = traverseAutoManager.GetTraversePath(normalized);
                if (traversePath != null)
                    traverseRestored = traverseOperations.TryEnsureTraverseAccess(traversePath);
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
        var ntfsState = grantsHelper.TryReadRightsForEntry(entry);
        if (ntfsState != null)
        {
            int aceCount = isDeny ? ntfsState.DirectDenyAceCount : ntfsState.DirectAllowAceCount;
            if (aceCount > 0)
            {
                bool isFolder = Directory.Exists(normalized);
                entry.SavedRights = SavedRightsComparer.FromNtfsState(ntfsState, isDeny, _isContainer, isFolder);
            }
        }

        _pending.Grants.AddGrant(entry);

        // Record target config if the user dropped onto a specific config section.
        if (hasExplicitTargetSection)
            _pending.Grants.MoveGrantConfig(entry, targetConfigPath);

        // 6. For allow grants: auto-add traverse entry for the grant path (folder grant) or its parent (file grant).
        bool traverseAdded = false;
        if (!isDeny)
        {
            var traversePath = traverseAutoManager.GetTraversePath(normalized);
            if (traversePath != null)
                traverseAdded = traverseOperations.TryEnsureTraverseAccess(traversePath);
        }

        _gridRefresher.RefreshGrantsGrid();
        if (traverseAdded)
            _gridRefresher.RefreshTraverseGrid();
        return null;
    }

    private bool TryQueueExistingGrantFix(
        AppDatabase database,
        string normalized,
        bool isDeny,
        string? targetConfigPath,
        bool hasExplicitTargetSection)
    {
        var entry = FindCommittedGrant(database, normalized, isDeny);
        if (entry?.SavedRights == null)
            return false;

        var ntfsState = grantsHelper.TryReadRightsForEntry(entry);
        if (ntfsState == null)
            return false;

        bool isFolder = Directory.Exists(normalized);
        if (SavedRightsComparer.Instance.MatchesSavedRights(entry, ntfsState, _isContainer, isFolder))
            return false;

        _pending.Grants.AddGrantFix(entry);
        if (hasExplicitTargetSection &&
            grantsHelper.TargetsDifferentConfigSection(entry, targetConfigPath))
            _pending.Grants.MoveGrantConfig(entry, targetConfigPath);

        _gridRefresher.RefreshGrantsGrid();
        return true;
    }

    private bool ExistsCommittedGrant(AppDatabase database, string normalized, bool isDeny) =>
        FindCommittedGrant(database, normalized, isDeny) != null;

    private GrantedPathEntry? FindCommittedGrant(AppDatabase database, string normalized, bool isDeny)
    {
        return database.GetAccount(_sid)?.Grants.FirstOrDefault(e =>
            string.Equals(e.Path, normalized, StringComparison.OrdinalIgnoreCase) &&
            e.IsDeny == isDeny &&
            !e.IsTraverseOnly &&
            !_pending.Grants.IsPendingRemove(normalized, isDeny) &&
            !_pending.Grants.IsUntrackGrant(normalized, isDeny));
    }
}
