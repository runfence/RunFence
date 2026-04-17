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
    private IWin32Window _owner = null!;

    public bool IsApplyInProgress { get; private set; }

    public void Initialize(
        AclManagerPendingChanges pending,
        string sid,
        bool isContainer,
        IWin32Window owner)
    {
        _pending = pending;
        _sid = sid;
        _isContainer = isContainer;
        _owner = owner;
    }

    /// <summary>
    /// Executes the full Apply pipeline. Called on the UI thread; NTFS operations run on a
    /// background thread via Task.Run (one call per operation to keep UI responsive).
    /// The Apply button and progress bar are managed here.
    /// </summary>
    public async Task ApplyAsync(
        ToolStripProgressBar progressBar,
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
        progressBar.Minimum = 0;
        progressBar.Maximum = total;
        progressBar.Value = 0;
        progressBar.Visible = true;

        var errors = new List<(string Path, string Error)>();
        int current = 0;

        try
        {
            // --- Phase 1: Removes (before adds to avoid NTFS conflicts) ---

            // NTFS removes run on background thread per call (OS-heavy).
            current = await RunPhaseAsync(pendingRemoves,
                e => pathGrantService.RemoveGrant(_sid, e.Path, e.IsDeny, updateFileSystem: true),
                "revert grant ACE for", errors, progressBar, current, total, background: true);

            current = await RunPhaseAsync(pendingTraverseRemoves,
                e => pathGrantService.RemoveTraverse(_sid, e.Path, updateFileSystem: true),
                "revert traverse ACE for", errors, progressBar, current, total, background: true);

            // Untracks are DB-only (no OS call) — run on UI thread directly.
            current = await RunPhaseAsync(pendingUntrackGrants,
                e => pathGrantService.RemoveGrant(_sid, e.Path, e.IsDeny, updateFileSystem: false),
                "untrack grant for", errors, progressBar, current, total, background: false);

            current = await RunPhaseAsync(pendingUntrackTraverse,
                e => pathGrantService.RemoveTraverse(_sid, e.Path, updateFileSystem: false),
                "untrack traverse for", errors, progressBar, current, total, background: false);

            // --- Phase 2: Adds and modifications ---

            current = await RunPhaseAsync(pendingAdds,
                e => { var ownerSid = ResolveOwnerSid(e); pathGrantService.AddGrant(_sid, e.Path, e.IsDeny, e.SavedRights, ownerSid); },
                "apply grant ACE for", errors, progressBar, current, total, background: true);

            current = await RunPhaseAsync(pendingTraverseAdds,
                e => pathGrantService.AddTraverse(_sid, e.Path),
                "apply traverse ACE for", errors, progressBar, current, total, background: true);

            foreach (var mod in pendingModifications)
            {
                var entry = mod.Entry;
                try
                {
                    // ownerSid is derived from the new (target) rights state.
                    var newRights = mod.NewRights ?? entry.SavedRights;
                    var ownerSid = ResolveOwnerSid(mod.NewIsDeny, newRights);
                    if (mod.WasIsDeny != mod.NewIsDeny)
                    {
                        // Mode switch (Allow↔Deny): RemoveGrant must see the entry in DB under its original
                        // IsDeny and original SavedRights (both remain unmodified until after the remove).
                        // SavedRights and IsDeny are applied only after RemoveGrant so the DB remove
                        // (FindGrantEntryInDb) and IU-revert logic operate on the correct pre-switch state.
                        await Task.Run(() =>
                        {
                            pathGrantService.RemoveGrant(_sid, entry.Path, mod.WasIsDeny, updateFileSystem: true);
                            if (mod.NewRights != null)
                                entry.SavedRights = mod.NewRights;
                            pathGrantService.AddGrant(_sid, entry.Path, mod.NewIsDeny, entry.SavedRights, ownerSid);
                        });
                        entry.IsDeny = mod.NewIsDeny;
                    }
                    else
                    {
                        // Rights-only change (or double-switch back to original mode): update in place.
                        entry.IsDeny = mod.NewIsDeny;
                        if (mod.NewRights != null)
                            entry.SavedRights = mod.NewRights;
                        await Task.Run(() => pathGrantService.UpdateGrant(_sid, entry.Path, mod.NewIsDeny, entry.SavedRights!, ownerSid));
                    }

                    // Reset ownership when: (a) allow grant had Own=true, now Own!=true; (b) deny grant with Own=true
                    var shouldResetOwner = !_isContainer && (
                        (!entry.IsDeny && mod.WasOwn && entry.SavedRights?.Own != true) ||
                        (entry.IsDeny && entry.SavedRights?.Own == true));
                    if (shouldResetOwner)
                        await Task.Run(() => pathGrantService.ResetOwner(entry.Path, recursive: false));
                }
                catch (Exception ex)
                {
                    log.Error($"Failed to apply modification for '{entry.Path}'", ex);
                    errors.Add((entry.Path, ex.Message));
                }

                ReportProgress(_owner, progressBar, ++current, total);
            }

            current = await RunPhaseAsync(pendingTraverseFixes,
                e => pathGrantService.FixTraverse(_sid, e.Path),
                "re-apply traverse ACE for", errors, progressBar, current, total, background: true);

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

            // Quick Access pin/unpin.
            var toPin = pendingAdds
                .Where(e => !e.IsDeny && !e.IsTraverseOnly)
                .Select(e => e.Path).ToList();
            if (toPin.Count > 0)
                quickAccessPinService.PinFolders(_sid, toPin);

            var toUnpin = pendingRemoves
                .Where(e => !e.IsDeny && !e.IsTraverseOnly)
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
        }
        finally
        {
            // --- Phase 4: UI cleanup ---
            _pending.Clear();
            IsApplyInProgress = false;
            setDialogEnabled(true);
            setApplyEnabled(_pending.HasPendingChanges);
            progressBar.Visible = false;
            refreshGrids();
        }

        if (errors.Count > 0)
        {
            var msg = string.Join("\n", errors.Select(e => $"  {e.Path}: {e.Error}"));
            MessageBox.Show($"The following operations failed (changes were partially applied):\n\n{msg}",
                "Apply Errors", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
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
        ToolStripProgressBar progressBar,
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
            ReportProgress(_owner, progressBar, ++current, total);
        }
        return current;
    }

    /// <summary>
    /// Returns the SID to assign as owner when applying an allow grant, or null if ownership
    /// should not be changed. Ownership is only changed for non-container allow grants where
    /// <see cref="SavedRightsState.Own"/> is set.
    /// </summary>
    private string? ResolveOwnerSid(GrantedPathEntry entry)
        => ResolveOwnerSid(entry.IsDeny, entry.SavedRights);

    private string? ResolveOwnerSid(bool isDeny, SavedRightsState? savedRights)
    {
        if (isDeny || _isContainer)
            return null;
        return savedRights?.Own == true ? _sid : null;
    }

    private static void ReportProgress(IWin32Window owner, ToolStripProgressBar bar, int c, int t)
    {
        if (owner is Control { IsDisposed: false } ctrl)
        {
            try
            {
                ctrl.BeginInvoke(() => { bar.Value = Math.Min(c, t); });
            }
            catch (ObjectDisposedException) { }
        }
    }
}
