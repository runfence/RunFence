using RunFence.Acl.QuickAccess;
using RunFence.Acl.Traverse;
using RunFence.Acl.UI.Forms;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Persistence;

namespace RunFence.Acl.UI;

/// <summary>
/// Orchestrates the Apply button logic for <see cref="AclManagerDialog"/>:
/// Phase 1 (NTFS removes) → Phase 2 (DB commit) → Phase 3 (NTFS adds/modifications).
/// When the SID is an AppContainer, all grant changes are also mirrored to the
/// interactive desktop user (who must independently pass the dual access check).
/// Tracks in-progress state to block closing while work is running.
/// </summary>
public class AclManagerApplyOrchestrator
{
    private readonly ILoggingService _log;
    private readonly AclManagerNtfsApplier _ntfsApplier;
    private readonly AclManagerDbCommitter _dbCommitter;
    private readonly AclManagerInteractiveUserSync _interactiveUserSync;
    private readonly IDatabaseProvider _databaseProvider;
    private readonly ISessionSaver _sessionSaver;
    private readonly IQuickAccessPinService _quickAccessPinService;
    private AclManagerPendingChanges _pending = null!;
    private string _sid = null!;
    private bool _isContainer;
    private IWin32Window _owner = null!;
    private string? _interactiveUserSid;

    public bool IsApplyInProgress { get; private set; }

    public AclManagerApplyOrchestrator(
        ILoggingService log,
        AclManagerNtfsApplier ntfsApplier,
        AclManagerDbCommitter dbCommitter,
        AclManagerInteractiveUserSync interactiveUserSync,
        IDatabaseProvider databaseProvider,
        ISessionSaver sessionSaver,
        IQuickAccessPinService quickAccessPinService)
    {
        _log = log;
        _ntfsApplier = ntfsApplier;
        _dbCommitter = dbCommitter;
        _interactiveUserSync = interactiveUserSync;
        _databaseProvider = databaseProvider;
        _sessionSaver = sessionSaver;
        _quickAccessPinService = quickAccessPinService;
    }

    public void Initialize(
        AclManagerPendingChanges pending,
        string sid,
        bool isContainer,
        IWin32Window owner,
        string? interactiveUserSid = null)
    {
        _pending = pending;
        _sid = sid;
        _isContainer = isContainer;
        _owner = owner;
        _interactiveUserSid = interactiveUserSid;
        if (interactiveUserSid != null)
            _interactiveUserSync.Initialize(interactiveUserSid);
    }

    /// <summary>
    /// Executes the full Apply pipeline. Called on the UI thread; NTFS operations run on a
    /// background thread via Task.Run. The Apply button and progress bar are managed here.
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
        var traverseAddedForModifications = new List<GrantedPathEntry>();

        int current = 0;

        try
        {
            // --- Phase 1: NTFS removes (background) ---
            var removeResult = await _ntfsApplier.RemoveEntriesAsync(
                pendingRemoves, pendingTraverseRemoves, errors, _owner, progressBar, current, total);
            current = removeResult.CurrentProgress;

            // --- Phase 2: DB commit (UI thread) ---
            _dbCommitter.CommitToDatabase(
                _sid, removeResult,
                pendingAdds, pendingTraverseAdds,
                pendingUntrackGrants, pendingUntrackTraverse,
                pendingConfigMoves, pendingTraverseConfigMoves,
                errors);

            current += pendingUntrackGrants.Count + pendingUntrackTraverse.Count;
            AclManagerNtfsApplier.ReportProgress(_owner, progressBar, current, total);

            // --- Phase 3: NTFS adds/modifications (background) ---
            var applyResult = await _ntfsApplier.ApplyEntriesAsync(
                pendingAdds, pendingTraverseAdds, pendingModifications, pendingTraverseFixes,
                _sid, _isContainer, errors, _owner, progressBar, current, total);

            // Set AllAppliedPaths on newly-added traverse entries (pendingTraverseAdds) now that we're
            // back on the UI thread. The entries are already committed to DB in Phase 2 (same object
            // references), so mutating AllAppliedPaths here is sufficient — no separate TrackPath needed.
            foreach (var (entry, visitedPaths) in applyResult.TraverseAddVisitedPaths)
            {
                if (visitedPaths.Count > 0)
                    entry.AllAppliedPaths = visitedPaths;
            }

            // Track traverse entries produced during Phase 3 modification processing into DB now that
            // we're back on the UI thread. Uses TraversePathsHelper.TrackPath for deduplication.
            if (applyResult.TraverseNtfsAppliedForModifications.Count > 0)
            {
                var traversePaths = TraversePathsHelper.GetOrCreateTraversePaths(_databaseProvider.GetDatabase(), _sid);
                foreach (var (entry, visitedPaths) in applyResult.TraverseNtfsAppliedForModifications)
                {
                    if (TraversePathsHelper.TrackPath(traversePaths, entry.Path, visitedPaths))
                        traverseAddedForModifications.Add(entry);
                }
            }

            // Persist AllAppliedPaths written by FixTraverseEntryNow, newly-added traverse entries'
            // path tracking, and any new traverse entries added from modification processing.
            if (pendingTraverseFixes.Count > 0 || applyResult.TraverseAddVisitedPaths.Count > 0 || traverseAddedForModifications.Count > 0)
            {
                try
                {
                    _sessionSaver.SaveConfig();
                }
                catch (Exception ex)
                {
                    _log.Error("Failed to persist traverse fix paths", ex);
                    errors.Add(("(database)", ex.Message));
                }
            }

            // Sync all grant/traverse changes to the interactive desktop user when managing a container.
            if (_interactiveUserSid != null)
                await _interactiveUserSync.SyncAsync(
                    pendingAdds, pendingRemoves, pendingModifications,
                    [..pendingTraverseAdds, ..traverseAddedForModifications],
                    pendingTraverseRemoves, pendingTraverseFixes,
                    pendingUntrackGrants, pendingUntrackTraverse,
                    removeResult.SuccessfulRemoves, removeResult.SuccessfulTraverseRemoves, errors);

            // Pin newly added Allow grants for directories
            var toPin = pendingAdds
                .Where(e => !e.IsDeny && !e.IsTraverseOnly)
                .Select(e => e.Path).ToList();
            if (toPin.Count > 0)
                _quickAccessPinService.PinFolders(_sid, toPin);

            // Unpin removed Allow grants
            var toUnpin = pendingRemoves
                .Where(e => !e.IsDeny && !e.IsTraverseOnly)
                .Select(e => e.Path).ToList();
            if (toUnpin.Count > 0)
                _quickAccessPinService.UnpinFolders(_sid, toUnpin);
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
}