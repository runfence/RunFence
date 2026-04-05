using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Persistence;

namespace RunFence.Acl.UI;

/// <summary>
/// Handles Phase 2 of the Apply pipeline: committing pending changes to the database on the UI thread.
/// Processes successful removes, untrack operations, adds, modifications, and config moves.
/// </summary>
public class AclManagerDbCommitter
{
    private readonly IGrantConfigTracker _grantConfigTracker;
    private readonly ILoggingService _log;
    private readonly IDatabaseProvider _databaseProvider;
    private readonly ISessionSaver _sessionSaver;

    public AclManagerDbCommitter(
        IGrantConfigTracker grantConfigTracker,
        ILoggingService log,
        IDatabaseProvider databaseProvider,
        ISessionSaver sessionSaver)
    {
        _grantConfigTracker = grantConfigTracker;
        _log = log;
        _databaseProvider = databaseProvider;
        _sessionSaver = sessionSaver;
    }

    /// <summary>
    /// Commits all pending changes to the database and saves. Runs on the UI thread.
    /// </summary>
    public void CommitToDatabase(
        string sid,
        NtfsRemoveResult removeResult,
        List<GrantedPathEntry> pendingAdds,
        List<GrantedPathEntry> pendingTraverseAdds,
        List<GrantedPathEntry> pendingUntrackGrants,
        List<GrantedPathEntry> pendingUntrackTraverse,
        IReadOnlyList<KeyValuePair<(string Path, bool IsDeny), string?>> pendingConfigMoves,
        IReadOnlyList<KeyValuePair<string, string?>> pendingTraverseConfigMoves,
        List<(string Path, string Error)> errors)
    {
        var db = _databaseProvider.GetDatabase();
        try
        {
            CommitRemovals(db, sid, removeResult.SuccessfulRemoves, removeResult.SuccessfulTraverseRemoves);
            CommitUntracks(db, sid, pendingUntrackGrants, pendingUntrackTraverse);
            CommitAddsAndModifications(db, sid, pendingAdds, pendingTraverseAdds);
            ApplyConfigMoves(db, sid, pendingConfigMoves, pendingTraverseConfigMoves);
            _sessionSaver.SaveConfig();
        }
        catch (Exception ex)
        {
            _log.Error("Failed to commit pending changes to database", ex);
            errors.Add(("(database)", ex.Message));
        }
    }

    private void CommitRemovals(
        AppDatabase db,
        string sid,
        HashSet<(string Path, bool IsDeny)> successfulRemoves,
        HashSet<string> successfulTraverseRemoves)
    {
        var entries = db.GetAccount(sid)?.Grants;
        if (entries == null)
            return;

        entries.RemoveAll(e =>
            !e.IsTraverseOnly &&
            successfulRemoves.Contains((e.Path, e.IsDeny)));

        entries.RemoveAll(e =>
            e.IsTraverseOnly &&
            successfulTraverseRemoves.Contains(e.Path));

        db.RemoveAccountIfEmpty(sid);
    }

    private static void CommitUntracks(
        AppDatabase db,
        string sid,
        List<GrantedPathEntry> pendingUntrackGrants,
        List<GrantedPathEntry> pendingUntrackTraverse)
    {
        if (pendingUntrackGrants.Count == 0 && pendingUntrackTraverse.Count == 0)
            return;
        var entries = db.GetAccount(sid)?.Grants;
        if (entries == null)
            return;

        foreach (var entry in pendingUntrackGrants)
        {
            entries.RemoveAll(e =>
                !e.IsTraverseOnly &&
                string.Equals(e.Path, entry.Path, StringComparison.OrdinalIgnoreCase) &&
                e.IsDeny == entry.IsDeny);
        }

        foreach (var entry in pendingUntrackTraverse)
        {
            entries.RemoveAll(e =>
                e.IsTraverseOnly &&
                string.Equals(e.Path, entry.Path, StringComparison.OrdinalIgnoreCase));
        }

        db.RemoveAccountIfEmpty(sid);
    }

    private static void CommitAddsAndModifications(
        AppDatabase db,
        string sid,
        List<GrantedPathEntry> pendingAdds,
        List<GrantedPathEntry> pendingTraverseAdds)
    {
        var entries = db.GetOrCreateAccount(sid).Grants;

        foreach (var entry in pendingAdds)
        {
            var existing = entries.FirstOrDefault(e =>
                string.Equals(e.Path, entry.Path, StringComparison.OrdinalIgnoreCase) &&
                e.IsDeny == entry.IsDeny && !e.IsTraverseOnly);
            if (existing != null)
                existing.SavedRights = entry.SavedRights;
            else
                entries.Add(entry);
        }

        foreach (var entry in pendingTraverseAdds)
        {
            bool alreadyExists = entries.Any(e =>
                e.IsTraverseOnly &&
                string.Equals(e.Path, entry.Path, StringComparison.OrdinalIgnoreCase));
            if (!alreadyExists)
                entries.Add(entry);
        }
    }

    private void ApplyConfigMoves(
        AppDatabase db,
        string sid,
        IReadOnlyList<KeyValuePair<(string Path, bool IsDeny), string?>> configMoves,
        IReadOnlyList<KeyValuePair<string, string?>> traverseConfigMoves)
    {
        if (configMoves.Count == 0 && traverseConfigMoves.Count == 0)
            return;
        var entries = db.GetAccount(sid)?.Grants;
        if (entries == null)
            return;

        foreach (var kvp in configMoves)
        {
            var (path, isDeny) = kvp.Key;
            var entry = entries.FirstOrDefault(e =>
                string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase) &&
                e.IsDeny == isDeny && !e.IsTraverseOnly);
            if (entry != null)
                _grantConfigTracker.AssignGrant(sid, entry, kvp.Value);
        }

        foreach (var kvp in traverseConfigMoves)
        {
            var entry = entries.FirstOrDefault(e =>
                e.IsTraverseOnly &&
                string.Equals(e.Path, kvp.Key, StringComparison.OrdinalIgnoreCase));
            if (entry != null)
                _grantConfigTracker.AssignGrant(sid, entry, kvp.Value);
        }
    }
}