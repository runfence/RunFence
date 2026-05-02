using System.Security.Principal;
using RunFence.Acl.Permissions;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;

namespace RunFence.Acl.Traverse;

/// <summary>
/// Performs per-SID traverse grant reconciliation: adds traverse ACEs when needed
/// and removes them when they become redundant due to group membership changes.
/// Used by <see cref="GrantReconciliationService"/> during the background reconciliation phase.
/// </summary>
public class SidReconciler(
    IAclPermissionService aclPermission,
    Func<AncestorTraverseGranter> ancestorTraverseGranterFactory,
    ILoggingService log,
    IInteractiveUserResolver interactiveUserResolver,
    IFileSystemPathInfo pathInfo)
{
    /// <summary>
    /// Reconciles traverse grants for a single SID given its new group memberships.
    /// Populates <paramref name="newTraverseEntries"/> and <paramref name="removedTraversePaths"/>.
    /// Runs on the background thread — does NOT access the database.
    /// </summary>
    public void ReconcileSid(
        string sid,
        IReadOnlyList<string> newGroups,
        Dictionary<string, List<(string Path, List<string> AppliedPaths)>> newTraverseEntries,
        HashSet<string> removedTraversePaths,
        IReadOnlyDictionary<string, List<GrantedPathEntry>>? accountGrants)
    {
        try
        {
            var identity = new SecurityIdentifier(sid);

            ReconcileLogonScript(sid, identity, newGroups, newTraverseEntries, removedTraversePaths, accountGrants);

            if (string.Equals(sid, interactiveUserResolver.GetInteractiveUserSid(), StringComparison.OrdinalIgnoreCase))
                ReconcileAppDirectory(sid, identity, newGroups, newTraverseEntries, removedTraversePaths, accountGrants);

            ReconcileDragBridgeTempRoot(sid, identity, newGroups, newTraverseEntries, removedTraversePaths, accountGrants);
        }
        catch (Exception ex)
        {
            log.Warn($"SidReconciler: reconciliation failed for '{sid}': {ex.Message}");
        }
    }

    private void ReconcileLogonScript(string sid, SecurityIdentifier identity, IReadOnlyList<string> groupSids,
        Dictionary<string, List<(string, List<string>)>> newTraverseEntries,
        HashSet<string> removedTraversePaths,
        IReadOnlyDictionary<string, List<GrantedPathEntry>>? accountGrants)
    {
        var scriptsDir = Path.Combine(PathConstants.ProgramDataDir, "scripts");
        var scriptFile = Path.Combine(scriptsDir, $"{sid}_block_login.cmd");
        ReconcileTraverseLocation(sid, identity, groupSids, scriptsDir, scriptFile,
            newTraverseEntries, removedTraversePaths, accountGrants);
    }

    private void ReconcileAppDirectory(string sid, SecurityIdentifier identity, IReadOnlyList<string> groupSids,
        Dictionary<string, List<(string, List<string>)>> newTraverseEntries,
        HashSet<string> removedTraversePaths,
        IReadOnlyDictionary<string, List<GrantedPathEntry>>? accountGrants)
    {
        var appDir = Path.GetDirectoryName(PathConstants.UnlockCmdPath);
        if (string.IsNullOrEmpty(appDir))
            return;
        ReconcileTraverseLocation(sid, identity, groupSids, appDir, null,
            newTraverseEntries, removedTraversePaths, accountGrants);
    }

    private void ReconcileDragBridgeTempRoot(string sid, SecurityIdentifier identity, IReadOnlyList<string> groupSids,
        Dictionary<string, List<(string, List<string>)>> newTraverseEntries,
        HashSet<string> removedTraversePaths,
        IReadOnlyDictionary<string, List<GrantedPathEntry>>? accountGrants)
    {
        var tempRoot = Path.Combine(PathConstants.ProgramDataDir, PathConstants.DragBridgeTempDir);
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
        if (!pathInfo.DirectoryExists(dirPath))
            return;
        if (prerequisiteFilePath != null && !pathInfo.FileExists(prerequisiteFilePath))
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

            var pathsToCheck = entry.AllAppliedPaths ?? [entry.Path];
            bool allCoveredByGroups = true;

            foreach (var appliedPath in pathsToCheck)
            {
                try
                {
                    if (!pathInfo.DirectoryExists(appliedPath))
                        continue;
                    var dirSecurity = pathInfo.GetDirectorySecurity(appliedPath);
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

            break;
        }
    }
}
