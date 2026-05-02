using RunFence.Acl.Permissions;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Persistence;

namespace RunFence.Acl.Traverse;

/// <summary>
/// Detects group membership changes for known SIDs and reconciles RunFence-managed traverse
/// ACEs that may no longer be needed (because a group grant now covers them) or are newly
/// needed (because a group was removed). Three-phase design for thread safety:
///
/// 1. <see cref="DetectGroupChanges"/> — UI thread, reads group membership, compares with snapshot.
/// 2. <see cref="ReconcileChangedSids"/> — background thread, delegates per-SID work to <see cref="SidReconciler"/>.
/// 3. <see cref="ApplyReconciliationResult"/> — UI thread, updates database without concurrent access.
///
/// Also provides <see cref="ReconcileIfGroupsChanged"/> for startup and UI refresh callers.
/// Per-SID reconciled locations are documented in <see cref="SidReconciler"/>.
/// </summary>
public class GrantReconciliationService(
    IAclPermissionService aclPermission,
    ILocalGroupMembershipService localGroupMembership,
    ILoggingService log,
    ISessionSaver sessionSaver,
    IDatabaseProvider databaseProvider,
    SidReconciler sidReconciler)
{
    /// <summary>Result of a reconciliation pass over a set of changed SIDs.</summary>
    public record ReconciliationResult(
        Dictionary<string, List<string>> UpdatedSnapshots,
        Dictionary<string, List<(string Path, List<string> AppliedPaths)>> NewTraverseEntries,
        Dictionary<string, HashSet<string>> RemovedTraversePaths);

    /// <summary>
    /// Compares current group memberships against the stored snapshot.
    /// Returns SIDs whose group membership has changed (or null snapshot = first run).
    /// </summary>
    public async Task<List<(string Sid, List<string> NewGroups)>> DetectGroupChanges()
    {
        var database = databaseProvider.GetDatabase();
        var changed = new List<(string, List<string>)>();

        database.AccountGroupSnapshots ??= new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        // .NET Core 3+ allows removing from Dictionary<TKey,TValue> during enumeration via foreach —
        // this does not throw InvalidOperationException unlike List<T> or older frameworks.
        foreach (var snapshot in database.AccountGroupSnapshots)
        {
            if (database.Accounts.All(x => x.Sid != snapshot.Key))
                database.AccountGroupSnapshots.Remove(snapshot.Key);
        }

        List<Task> tasks = new();

        foreach (var sid0 in database.Accounts.Select(x => x.Sid))
        {
            var sid = sid0;

            // Skip AppContainer SIDs — they have fixed group membership
            if (AclHelper.IsContainerSid(sid) || AclHelper.IsLowIntegritySid(sid))
                continue;

            // Skip local groups — group membership snapshots don't apply to groups themselves
            if (localGroupMembership.IsLocalGroup(sid))
                continue;

            tasks.Add(AsyncPart());
            async Task AsyncPart()
            {
                List<string> currentGroups;
                try
                {
                    currentGroups = await Task.Run(() => aclPermission.ResolveAccountGroupSids(sid));
                    // came back to UI context
                }
                catch (Exception ex)
                {
                    log.Warn($"GrantReconciliationService: group resolution failed for '{sid}': {ex.Message}");
                    return;
                }

                if (!database.AccountGroupSnapshots.TryGetValue(sid, out var snapshot))
                {
                    // First time we see this SID — populate snapshot without reconciling
                    database.AccountGroupSnapshots[sid] = currentGroups;
                    return;
                }

                if (!new HashSet<string>(snapshot, StringComparer.OrdinalIgnoreCase).SetEquals(currentGroups))
                    changed.Add((sid, currentGroups));
            }
        }

        await Task.WhenAll(tasks); 
        return changed;
    }

    /// <summary>
    /// Performs filesystem ACL reads/writes for SIDs with changed group membership.
    /// Returns a result record describing what to apply to the database.
    /// Runs on a background thread — does NOT modify the database.
    /// </summary>
    public ReconciliationResult ReconcileChangedSids(
        List<(string Sid, List<string> NewGroups)> changedSids,
        IReadOnlyDictionary<string, List<GrantedPathEntry>>? accountGrants = null)
    {
        var updatedSnapshots = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var newTraverseEntries = new Dictionary<string, List<(string, List<string>)>>(StringComparer.OrdinalIgnoreCase);
        var removedTraversePaths = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var (sid, newGroups) in changedSids)
        {
            updatedSnapshots[sid] = newGroups;
            var sidRemovedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            sidReconciler.ReconcileSid(sid, newGroups, newTraverseEntries, sidRemovedPaths, accountGrants);
            if (sidRemovedPaths.Count > 0)
                removedTraversePaths[sid] = sidRemovedPaths;
        }

        return new ReconciliationResult(updatedSnapshots, newTraverseEntries, removedTraversePaths);
    }

    /// <summary>
    /// Applies the reconciliation result to the database. Runs on the UI thread.
    /// </summary>
    public void ApplyReconciliationResult(ReconciliationResult result)
    {
        var database = databaseProvider.GetDatabase();
        database.AccountGroupSnapshots ??= new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (sid, groups) in result.UpdatedSnapshots)
            database.AccountGroupSnapshots[sid] = groups;

        if (result.RemovedTraversePaths.Count > 0)
        {
            foreach (var (sid, removedPaths) in result.RemovedTraversePaths)
            {
                var entries = database.GetAccount(sid)?.Grants;
                if (entries == null)
                    continue;
                entries.RemoveAll(e => e.IsTraverseOnly &&
                                       removedPaths.Contains(Path.GetFullPath(e.Path)));
                database.RemoveAccountIfEmpty(sid);
            }
        }

        foreach (var (sid, entries) in result.NewTraverseEntries)
        {
            var traversePaths = TraversePathsHelper.GetOrCreateTraversePaths(database, sid);
            foreach (var (path, appliedPaths) in entries)
                TraversePathsHelper.TrackPath(traversePaths, path, appliedPaths);
        }

        sessionSaver.SaveConfig();
    }

    /// <summary>
    /// Wrapper for startup and UI refresh calls.
    /// Detects changes on the UI thread, reconciles filesystem ACLs on a background thread,
    /// then applies results to the database on the UI thread.
    /// </summary>
    public async Task<bool> ReconcileIfGroupsChanged()
    {
        var changed = await DetectGroupChanges();
        if (changed.Count == 0)
            return false;

        var database = databaseProvider.GetDatabase();
        var accountGrants = database.Accounts.ToDictionary(a => a.Sid, a => a.Grants.ToList(), StringComparer.OrdinalIgnoreCase);
        var result = await Task.Run(() => ReconcileChangedSids(changed, accountGrants));
        ApplyReconciliationResult(result);
        return true;
    }
}