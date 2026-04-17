using System.Security.Principal;
using RunFence.Acl;
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
/// 2. <see cref="ReconcileChangedSids"/> — background thread, does filesystem ACL reads/writes only.
/// 3. <see cref="ApplyReconciliationResult"/> — UI thread, updates database without concurrent access.
///
/// Also provides <see cref="ReconcileIfGroupsChanged"/> for startup and UI refresh callers.
///
/// Automated locations reconciled (per SID):
///   1. Logon script directory + ancestors — if {SID}_block_login.cmd exists in scripts dir
///   2. App directory (unlock.cmd parent) — if SID == interactive user SID
///   3. DragBridge TempRoot + ancestors — if SID has a DragBridge traverse entry
/// </summary>
public class GrantReconciliationService(
    IAclPermissionService aclPermission,
    ILocalGroupMembershipService localGroupMembership,
    ILoggingService log,
    ISessionSaver sessionSaver,
    IDatabaseProvider databaseProvider,
    Func<AncestorTraverseGranter> ancestorTraverseGranterFactory,
    IInteractiveUserResolver interactiveUserResolver)
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
            if (AclHelper.IsContainerSid(sid))
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
            ReconcileTraverseForSid(sid, newGroups, newTraverseEntries, sidRemovedPaths, accountGrants);
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

    private void ReconcileTraverseForSid(
        string sid,
        IReadOnlyList<string> newGroups,
        Dictionary<string, List<(string, List<string>)>> newTraverseEntries,
        HashSet<string> removedTraversePaths,
        IReadOnlyDictionary<string, List<GrantedPathEntry>>? accountGrants)
    {
        try
        {
            var identity = new SecurityIdentifier(sid);

            // 1. Logon script directory
            ReconcileLogonScript(sid, identity, newGroups, newTraverseEntries, removedTraversePaths, accountGrants);

            // 2. App directory (unlock.cmd parent) — interactive user only
            if (string.Equals(sid, interactiveUserResolver.GetInteractiveUserSid(), StringComparison.OrdinalIgnoreCase))
                ReconcileAppDirectory(sid, identity, newGroups, newTraverseEntries, removedTraversePaths, accountGrants);

            // 3. DragBridge temp root
            ReconcileDragBridgeTempRoot(sid, identity, newGroups, newTraverseEntries, removedTraversePaths, accountGrants);
        }
        catch (Exception ex)
        {
            log.Warn($"GrantReconciliationService: reconciliation failed for '{sid}': {ex.Message}");
        }
    }

    private void ReconcileLogonScript(string sid, SecurityIdentifier identity, IReadOnlyList<string> groupSids,
        Dictionary<string, List<(string, List<string>)>> newTraverseEntries,
        HashSet<string> removedTraversePaths,
        IReadOnlyDictionary<string, List<GrantedPathEntry>>? accountGrants = null)
    {
        var scriptsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "RunFence", "scripts");
        var scriptFile = Path.Combine(scriptsDir, $"{sid}_block_login.cmd");
        ReconcileTraverseLocation(sid, identity, groupSids, scriptsDir, scriptFile,
            newTraverseEntries, removedTraversePaths, accountGrants);
    }

    private void ReconcileAppDirectory(string sid, SecurityIdentifier identity, IReadOnlyList<string> groupSids,
        Dictionary<string, List<(string, List<string>)>> newTraverseEntries,
        HashSet<string> removedTraversePaths,
        IReadOnlyDictionary<string, List<GrantedPathEntry>>? accountGrants = null)
    {
        var appDir = Path.GetDirectoryName(Constants.UnlockCmdPath);
        if (string.IsNullOrEmpty(appDir))
            return;
        ReconcileTraverseLocation(sid, identity, groupSids, appDir, null,
            newTraverseEntries, removedTraversePaths, accountGrants);
    }

    private void ReconcileDragBridgeTempRoot(string sid, SecurityIdentifier identity, IReadOnlyList<string> groupSids,
        Dictionary<string, List<(string, List<string>)>> newTraverseEntries,
        HashSet<string> removedTraversePaths,
        IReadOnlyDictionary<string, List<GrantedPathEntry>>? accountGrants = null)
    {
        var tempRoot = Path.Combine(Constants.ProgramDataDir, Constants.DragBridgeTempDir);
        ReconcileTraverseLocation(sid, identity, groupSids, tempRoot, null,
            newTraverseEntries, removedTraversePaths, accountGrants);
    }

    private void ReconcileTraverseLocation(
        string sid, SecurityIdentifier identity, IReadOnlyList<string> groupSids,
        string dirPath, string? prerequisiteFilePath,
        Dictionary<string, List<(string, List<string>)>> newTraverseEntries,
        HashSet<string> removedTraversePaths,
        IReadOnlyDictionary<string, List<GrantedPathEntry>>? accountGrants)
    {
        if (!Directory.Exists(dirPath))
            return;
        if (prerequisiteFilePath != null && !File.Exists(prerequisiteFilePath))
            return;

        var granter = ancestorTraverseGranterFactory();
        var (appliedPaths, anyAceAdded) = granter.GrantOnPathAndAncestors(dirPath, identity, groupSids: groupSids);
        if (anyAceAdded)
        {
            if (!newTraverseEntries.TryGetValue(sid, out var entries))
            {
                entries = [];
                newTraverseEntries[sid] = entries;
            }

            entries.Add((dirPath, appliedPaths));
        }

        CheckRedundantTraverse(sid, dirPath, groupSids, granter, removedTraversePaths, accountGrants);
    }

    /// <summary>
    /// Checks if an existing traverse entry for <paramref name="sid"/> on <paramref name="path"/>
    /// is now redundant because the SID's groups provide traverse rights on all applied paths
    /// without needing the SID's own direct ACE. If redundant, adds to <paramref name="removedTraversePaths"/>
    /// and reverts the SID's direct traverse ACEs.
    /// </summary>
    private void CheckRedundantTraverse(
        string sid,
        string path,
        IReadOnlyList<string> groupSids,
        AncestorTraverseGranter granter,
        HashSet<string> removedTraversePaths,
        IReadOnlyDictionary<string, List<GrantedPathEntry>>? accountGrants)
    {
        if (accountGrants == null || groupSids.Count == 0)
            return;
        if (!accountGrants.TryGetValue(sid, out var entries))
            return;

        var traverseRights = TraverseRightsHelper.TraverseRights;

        var normalizedPath = Path.GetFullPath(path);
        foreach (var entry in entries)
        {
            if (!entry.IsTraverseOnly)
                continue;
            if (!string.Equals(Path.GetFullPath(entry.Path), normalizedPath, StringComparison.OrdinalIgnoreCase))
                continue;

            // Check if groups alone provide traverse on all applied paths (without the SID's own ACE).
            var pathsToCheck = entry.AllAppliedPaths ?? [entry.Path];
            bool allCoveredByGroups = true;

            foreach (var appliedPath in pathsToCheck)
            {
                try
                {
                    if (!Directory.Exists(appliedPath))
                        continue;
                    var dirSecurity = new DirectoryInfo(appliedPath).GetAccessControl();
                    // Pass empty string as accountSid so only group SIDs are checked —
                    // this verifies groups alone provide access without the direct ACE.
                    if (!aclPermission.HasEffectiveRights(dirSecurity, "", groupSids, traverseRights))
                    {
                        allCoveredByGroups = false;
                        break;
                    }
                }
                catch
                {
                    allCoveredByGroups = false;
                    break;
                }
            }

            if (allCoveredByGroups)
            {
                removedTraversePaths.Add(normalizedPath);

                // Revert the SID's direct traverse ACEs on NTFS
                var identity = new SecurityIdentifier(sid);
                foreach (var appliedPath in pathsToCheck)
                {
                    try
                    {
                        granter.RemoveAce(appliedPath, identity);
                    }
                    catch (Exception ex)
                    {
                        log.Warn($"Failed to remove redundant traverse ACE on '{appliedPath}': {ex.Message}");
                    }
                }
            }

            break; // Only one traverse entry per path
        }
    }
}