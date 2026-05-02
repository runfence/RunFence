using RunFence.Acl.QuickAccess;
using RunFence.Acl.UI.Forms;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Persistence;

namespace RunFence.Acl.UI;

/// <summary>
/// Orchestrates the Apply button logic for <see cref="AclManagerDialog"/>:
/// Phase 1 (removes) → Phase 2 (adds/modifications) → Phase 3 (post-processing).
/// All ACL and DB work is delegated to <see cref="IPathGrantService"/>, which handles
/// NTFS ACE operations, DB writes, and container interactive-user sync atomically per call.
/// Tracks in-progress state to block closing while work is running.
/// </summary>
public class AclManagerApplyOrchestrator(
    ILoggingService log,
    IPathGrantService pathGrantService,
    IGrantConfigTracker grantConfigTracker,
    IDatabaseProvider databaseProvider,
    ISessionSaver sessionSaver,
    IQuickAccessPinService quickAccessPinService)
{
    private AclManagerPendingChanges _pending = null!;
    private string _sid = null!;
    private bool _isContainer;

    public bool IsApplyInProgress { get; private set; }

    public void Initialize(
        AclManagerPendingChanges pending,
        string sid,
        bool isContainer)
    {
        _pending = pending;
        _sid = sid;
        _isContainer = isContainer;
    }

    /// <summary>
    /// Executes the full Apply pipeline. Called on the UI thread; NTFS operations run on a
    /// background thread via Task.Run (one call per operation to keep UI responsive).
    /// The Apply button and progress bar are managed by the caller via <paramref name="progress"/>
    /// and <paramref name="setApplyEnabled"/>/<paramref name="setDialogEnabled"/>.
    /// </summary>
    public async Task ApplyAsync(
        IProgress<(int current, int total)> progress,
        Action<bool> setApplyEnabled,
        Action<bool> setDialogEnabled,
        Action refreshGrids)
    {
        if (IsApplyInProgress)
            return;

        // Snapshot pending state before any async operations so we work on a stable copy.
        var pendingAdds = _pending.PendingAdds.Values.ToList();
        var pendingRemoves = _pending.PendingRemoves.Values.ToList();
        var pendingModifications = _pending.PendingModifications.Values.ToList();
        var pendingTraverseAdds = _pending.PendingTraverseAdds.Values.ToList();
        var pendingTraverseRemoves = _pending.PendingTraverseRemoves.Values.ToList();
        var pendingTraverseFixes = _pending.PendingTraverseFixes.Values.ToList();
        var pendingUntrackGrants = _pending.PendingUntrackGrants.Values.ToList();
        var pendingUntrackTraverse = _pending.PendingUntrackTraverse.Values.ToList();
        var pendingConfigMoves = _pending.PendingConfigMoves.ToList();
        var pendingTraverseConfigMoves = _pending.PendingTraverseConfigMoves.ToList();

        int total = pendingRemoves.Count + pendingTraverseRemoves.Count +
                    pendingAdds.Count + pendingTraverseAdds.Count +
                    pendingModifications.Count + pendingTraverseFixes.Count +
                    pendingUntrackGrants.Count + pendingUntrackTraverse.Count;
        if (total == 0 && pendingConfigMoves.Count == 0 && pendingTraverseConfigMoves.Count == 0)
            return;

        IsApplyInProgress = true;
        setDialogEnabled(false);
        setApplyEnabled(false);
        progress.Report((0, total));

        var errors = new List<(string Path, string Error)>();
        int current = 0;

        try
        {
            // --- Phase 1: Removes (before adds to avoid NTFS conflicts) ---

            // NTFS removes run on background thread per call (OS-heavy).
            current = await RunPhaseAsync(pendingRemoves,
                e => pathGrantService.RemoveGrant(_sid, e.Path, e.IsDeny, updateFileSystem: true),
                "revert grant ACE for", errors, progress, current, total, background: true);

            current = await RunPhaseAsync(pendingTraverseRemoves,
                e => pathGrantService.RemoveTraverse(_sid, e.Path, updateFileSystem: true),
                "revert traverse ACE for", errors, progress, current, total, background: true);

            // Untracks are DB-only (no OS call) — run on UI thread directly.
            current = await RunPhaseAsync(pendingUntrackGrants,
                e => pathGrantService.RemoveGrant(_sid, e.Path, e.IsDeny, updateFileSystem: false),
                "untrack grant for", errors, progress, current, total, background: false);

            current = await RunPhaseAsync(pendingUntrackTraverse,
                e => pathGrantService.RemoveTraverse(_sid, e.Path, updateFileSystem: false),
                "untrack traverse for", errors, progress, current, total, background: false);

            // --- Phase 2: Adds and modifications ---

            current = await RunPhaseAsync(pendingAdds,
                e =>
                {
                    e.SavedRights = AclHelper.ClearBlockedGrantOwner(_sid, _isContainer, e.SavedRights);
                    var ownerSid = ResolveOwnerSid(e);
                    pathGrantService.AddGrant(_sid, e.Path, e.IsDeny, e.SavedRights, ownerSid);
                },
                "apply grant ACE for", errors, progress, current, total, background: true);

            current = await RunModificationPhaseAsync(pendingModifications, errors, progress, current, total);

            current = await RunPhaseAsync(pendingTraverseAdds,
                e => pathGrantService.AddTraverse(_sid, e.Path),
                "apply traverse ACE for", errors, progress, current, total, background: true);

            current = await RunPhaseAsync(pendingTraverseFixes,
                e => pathGrantService.FixTraverse(_sid, e.Path),
                "re-apply traverse ACE for", errors, progress, current, total, background: true);

            // --- Phase 3: Post-processing (UI thread) ---

            // Config moves via IGrantConfigTracker.
            var db = databaseProvider.GetDatabase();
            var dbEntries = db.GetAccount(_sid)?.Grants;

            if (dbEntries != null)
            {
                foreach (var kvp in pendingConfigMoves)
                {
                    var (path, isDeny) = kvp.Key;
                    var entry = dbEntries.FirstOrDefault(e =>
                        string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase) &&
                        e.IsDeny == isDeny && !e.IsTraverseOnly);
                    if (entry != null)
                        grantConfigTracker.AssignGrant(_sid, entry, kvp.Value);
                }

                foreach (var kvp in pendingTraverseConfigMoves)
                {
                    var entry = dbEntries.FirstOrDefault(e =>
                        e.IsTraverseOnly &&
                        string.Equals(e.Path, kvp.Key, StringComparison.OrdinalIgnoreCase));
                    if (entry != null)
                        grantConfigTracker.AssignGrant(_sid, entry, kvp.Value);
                }
            }

            if (_isContainer)
            {
                foreach (var kvp in pendingTraverseConfigMoves)
                {
                    var sharedEntry = db.SharedContainerTraverseGrants.FirstOrDefault(e =>
                        e.IsTraverseOnly &&
                        string.Equals(e.Path, kvp.Key, StringComparison.OrdinalIgnoreCase));
                    if (sharedEntry != null)
                        grantConfigTracker.AssignGrant(
                            WellKnownSecuritySids.AllApplicationPackagesSid,
                            sharedEntry,
                            kvp.Value);
                }
            }

            // Quick Access pin/unpin.
            var toPin = pendingAdds
                .Where(e => e is { IsDeny: false, IsTraverseOnly: false })
                .Select(e => e.Path).ToList();
            if (toPin.Count > 0)
                quickAccessPinService.PinFolders(_sid, toPin);

            var toUnpin = pendingRemoves
                .Where(e => e is { IsDeny: false, IsTraverseOnly: false })
                .Select(e => e.Path).ToList();
            if (toUnpin.Count > 0)
                quickAccessPinService.UnpinFolders(_sid, toUnpin);

            try
            {
                sessionSaver.SaveConfig();
            }
            catch (Exception ex)
            {
                log.Error("Failed to persist applied changes", ex);
                errors.Add(("(database)", ex.Message));
            }

            // Clear pending state only when all operations succeeded; on partial failure, leave
            // state intact so the user can retry without losing which changes are still pending.
            if (errors.Count == 0)
                _pending.Clear();
        }
        finally
        {
            // --- Phase 4: UI cleanup ---
            IsApplyInProgress = false;
            setDialogEnabled(true);
            setApplyEnabled(_pending.HasPendingChanges);
            refreshGrids();
        }

        if (errors.Count > 0)
            ShowApplyErrors(errors);
    }

    /// <summary>
    /// Shows a dialog listing the failed apply operations. Virtual to allow subclasses
    /// to customize error reporting behavior.
    /// </summary>
    protected virtual void ShowApplyErrors(List<(string Path, string Error)> errors)
    {
        var msg = string.Join("\n", errors.Select(e => $"  {e.Path}: {e.Error}"));
        MessageBox.Show($"The following operations failed (changes were partially applied):\n\n{msg}",
            "Apply Errors", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    /// <summary>
    /// Runs the modifications loop (mode switches and rights-only changes), encapsulating
    /// progress reporting and error collection in the same style as <see cref="RunPhaseAsync"/>.
    /// Returns the updated progress counter.
    /// </summary>
    private async Task<int> RunModificationPhaseAsync(
        IEnumerable<PendingModification> modifications,
        List<(string Path, string Error)> errors,
        IProgress<(int current, int total)> progress,
        int current, int total)
    {
        foreach (var mod in modifications)
        {
            var entry = mod.Entry;
            try
            {
                // ownerSid is derived from the new (target) rights state.
                var newRights = AclHelper.ClearBlockedGrantOwner(_sid, _isContainer, mod.NewRights ?? entry.SavedRights);
                var ownerSid = ResolveOwnerSid(mod.NewIsDeny, newRights);
                if (mod.WasIsDeny != mod.NewIsDeny)
                {
                    // Mode switch (Allow↔Deny): RemoveGrant must see the entry in DB under its original
                    // IsDeny and original SavedRights (both remain unmodified until after the remove).
                    // SavedRights and IsDeny are applied only after RemoveGrant so the DB remove
                    // (FindGrantEntryInDb) and IU-revert logic operate on the correct pre-switch state.
                    // Capture original rights before mutation so we can restore them if AddGrant fails.
                    var originalRights = entry.SavedRights;
                    await Task.Run(() =>
                    {
                        pathGrantService.RemoveGrant(_sid, entry.Path, mod.WasIsDeny, updateFileSystem: true);
                        if (newRights != null)
                            entry.SavedRights = newRights;
                        try
                        {
                            pathGrantService.AddGrant(_sid, entry.Path, mod.NewIsDeny, entry.SavedRights, ownerSid);
                        }
                        catch (Exception addEx)
                        {
                            // AddGrant failed after RemoveGrant succeeded — the entry has been removed from NTFS.
                            // Revert the SavedRights mutation and attempt to restore the original grant so the
                            // user is not left without any grant on this path.
                            entry.SavedRights = originalRights;
                            try
                            {
                                pathGrantService.AddGrant(_sid, entry.Path, mod.WasIsDeny, entry.SavedRights, null);
                            }
                            catch (Exception restoreEx)
                            {
                                // Both the new grant and the restoration failed — manual intervention may be needed.
                                throw new AggregateException(
                                    $"AddGrant failed and restoration of original grant also failed for '{entry.Path}'",
                                    addEx, restoreEx);
                            }
                            throw;
                        }
                    });
                    entry.IsDeny = mod.NewIsDeny;
                }
                else
                {
                    // Rights-only change (or double-switch back to original mode): update in place.
                    entry.IsDeny = mod.NewIsDeny;
                    if (newRights != null)
                        entry.SavedRights = newRights;
                    await Task.Run(() => pathGrantService.UpdateGrant(_sid, entry.Path, mod.NewIsDeny, entry.SavedRights!, ownerSid));
                }

                // Reset ownership when: (a) allow grant had Own=true, now Own!=true;
                // (b) deny grant with Own=true; (c) allow+own switched to deny (SavedRights.Own
                // is already updated to deny defaults before this check, so we use mod.WasOwn)
                var shouldResetOwner = AclHelper.CanAssignGrantOwner(_sid, _isContainer) && (
                    (!entry.IsDeny && mod.WasOwn && entry.SavedRights?.Own != true) ||
                    entry is { IsDeny: true, SavedRights.Own: true } ||
                    (mod.WasIsDeny != mod.NewIsDeny && mod is { WasOwn: true, WasIsDeny: false }));
                if (shouldResetOwner)
                    await Task.Run(() => pathGrantService.ResetOwner(entry.Path, recursive: false));
            }
            catch (Exception ex)
            {
                log.Error($"Failed to apply modification for '{entry.Path}'", ex);
                var errorMessage = ex is AggregateException agg
                    ? string.Join("; ", agg.InnerExceptions.Select(e => e.Message))
                    : ex.Message;
                errors.Add((entry.Path, errorMessage));
            }

            progress.Report((++current, total));
        }

        return current;
    }

    /// <summary>
    /// Runs an operation for each entry, catching and recording errors, and reporting progress.
    /// When <paramref name="background"/> is true, each operation runs on a background thread via
    /// <c>Task.Run</c> (for OS-heavy NTFS calls); otherwise it runs synchronously on the UI thread.
    /// Returns the updated progress counter.
    /// </summary>
    private async Task<int> RunPhaseAsync(
        IEnumerable<GrantedPathEntry> items,
        Action<GrantedPathEntry> operation,
        string errorContext,
        List<(string Path, string Error)> errors,
        IProgress<(int current, int total)> progress,
        int current, int total,
        bool background)
    {
        foreach (var entry in items)
        {
            try
            {
                if (background)
                    await Task.Run(() => operation(entry));
                else
                    operation(entry);
            }
            catch (Exception ex)
            {
                log.Error($"Failed to {errorContext} '{entry.Path}'", ex);
                errors.Add((entry.Path, ex.Message));
            }
            progress.Report((++current, total));
        }
        return current;
    }

    /// <summary>
    /// Returns the SID to assign as owner when applying an allow grant, or null if ownership
    /// should not be changed. Ownership is only changed for owner-capable allow grants where
    /// <see cref="SavedRightsState.Own"/> is set.
    /// </summary>
    private string? ResolveOwnerSid(GrantedPathEntry entry)
        => ResolveOwnerSid(entry.IsDeny, entry.SavedRights);

    private string? ResolveOwnerSid(bool isDeny, SavedRightsState? savedRights)
    {
        if (isDeny || !AclHelper.CanAssignGrantOwner(_sid, _isContainer))
            return null;
        return savedRights?.Own == true ? _sid : null;
    }

}
