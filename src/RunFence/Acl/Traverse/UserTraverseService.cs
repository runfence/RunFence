using System.Security.Principal;
using RunFence.Acl.Permissions;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Persistence;

namespace RunFence.Acl.Traverse;

/// <summary>
/// Manages traverse ACE grants and reverts on ancestor directories for regular user SIDs.
/// User tokens have SeChangeNotifyPrivilege which normally bypasses the OS traverse check,
/// but we still add it for tool compatibility (along with ReadAttributes and Synchronize).
/// </summary>
public interface IUserTraverseService
{
    /// <summary>
    /// Grants traverse ACEs on <paramref name="path"/> and its ancestors and tracks them in the database.
    /// Returns <c>Modified = true</c> when a new DB entry was tracked, and
    /// <c>VisitedPaths</c> = the full list of ancestor directories visited (for <see cref="GrantedPathEntry.AllAppliedPaths"/> refresh).
    /// </summary>
    (bool Modified, List<string> VisitedPaths) EnsureTraverseAccess(string userSid, string path);

    void RevertTraverseAccessForPath(string userSid, GrantedPathEntry entryToRemove, AppDatabase database);
    void RevertTraverseAccess(string userSid, AppDatabase database);
}

public class UserTraverseService(ILoggingService log, IAclPermissionService aclPermission, AncestorTraverseGranter granter, IDatabaseProvider databaseProvider, IUiThreadInvoker uiThreadInvoker)
    : IUserTraverseService
{
    public (bool Modified, List<string> VisitedPaths) EnsureTraverseAccess(string userSid, string path)
    {
        log.Info($"EnsureUserTraverseAccess: sid='{userSid}' path='{path}'");
        List<string> appliedPaths;
        bool anyAceAdded;
        try
        {
            var userIdentity = new SecurityIdentifier(userSid);
            // Resolve group SIDs for effective access check (avoids redundant ACEs on dirs
            // where inheritance or group membership already provides traverse access).
            var groupSids = aclPermission.ResolveAccountGroupSids(userSid);
            (appliedPaths, anyAceAdded) = granter.GrantOnPathAndAncestors(path, userIdentity, groupSids);
        }
        catch (Exception ex)
        {
            log.Warn($"Failed to grant user traverse for '{userSid}' on '{path}': {ex.Message}");
            return (false, []);
        }

        // ACEs are always applied synchronously above. DB tracking is dispatched to the UI thread
        // to keep database writes thread-safe when called from a background thread.
        if (!anyAceAdded)
            return (false, appliedPaths);

        bool modified = false;
        uiThreadInvoker.RunOnUiThread(() =>
        {
            var database = databaseProvider.GetDatabase();
            var traversePaths = TraversePathsHelper.GetOrCreateTraversePaths(database, userSid);
            modified = TraversePathsHelper.TrackPath(traversePaths, path, appliedPaths);
        });
        return (modified, appliedPaths);
    }

    public void RevertTraverseAccessForPath(string userSid, GrantedPathEntry entryToRemove, AppDatabase database)
    {
        log.Info($"RevertUserTraverseAccessForPath: sid='{userSid}' path='{entryToRemove.Path}'");
        try
        {
            var userIdentity = new SecurityIdentifier(userSid);
            var traversePaths = TraversePathsHelper.GetTraversePaths(database, userSid);

            var normalizedPath = Path.GetFullPath(entryToRemove.Path);
            var remaining = traversePaths
                .Where(e => e.IsTraverseOnly &&
                            !string.Equals(Path.GetFullPath(e.Path), normalizedPath, StringComparison.OrdinalIgnoreCase));

            // Protect paths that also have regular allow grants: ACE removal would wipe both
            // the traverse ACE and the allow-grant ACE since REVOKE_ACCESS removes all allow ACEs.
            var protectedPaths = traversePaths
                .Where(e => e is { IsTraverseOnly: false, IsDeny: false })
                .Select(e => Path.GetFullPath(e.Path))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            granter.RevertForPath(userIdentity, entryToRemove, remaining, additionalStillNeeded: protectedPaths);
        }
        catch (Exception ex)
        {
            log.Warn($"Failed to revert user traverse for path '{entryToRemove.Path}': {ex.Message}");
        }
    }

    public void RevertTraverseAccess(string userSid, AppDatabase database)
    {
        log.Info($"RevertUserTraverseAccess: sid='{userSid}'");
        try
        {
            var userIdentity = new SecurityIdentifier(userSid);
            var traversePaths = TraversePathsHelper.GetTraversePaths(database, userSid);
            var dirsToClean = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in traversePaths.Where(e => e.IsTraverseOnly))
                TraversePathsHelper.CollectPaths(entry, dirsToClean);

            foreach (var dir in dirsToClean)
                granter.RemoveAce(dir, userIdentity);
        }
        catch (Exception ex)
        {
            log.Warn($"Failed to revert user traverse for '{userSid}': {ex.Message}");
        }
    }
}