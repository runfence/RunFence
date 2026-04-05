using System.Security.Principal;
using RunFence.Acl.Permissions;
using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.Acl.Traverse;

/// <summary>
/// Grants and reverts traverse ACEs on the logon scripts directory and its ancestors
/// so the blocked user's token can reach the logon script through paths with broken NTFS inheritance.
/// </summary>
public class LogonScriptTraverseGranter(
    AncestorTraverseGranter traverseGranter,
    IAclPermissionService aclPermission,
    ILoggingService log)
{
    /// <summary>
    /// Grants traverse access on <paramref name="scriptsDirPath"/> and all ancestors for <paramref name="sid"/>.
    /// Returns the list of paths where ACEs were added, or null if no ACEs were added or no traverseGranter.
    /// </summary>
    public List<string>? GrantTraverseAccess(string sid, string scriptsDirPath)
    {
        try
        {
            var userIdentity = new SecurityIdentifier(sid);
            var groupSids = aclPermission.ResolveAccountGroupSids(sid);
            var (appliedPaths, anyAceAdded) = traverseGranter.GrantOnPathAndAncestors(
                scriptsDirPath, userIdentity, groupSids: groupSids);
            return anyAceAdded ? appliedPaths : null;
        }
        catch (Exception ex)
        {
            log.Warn($"LogonScriptTraverseGranter: traverse grant failed for '{sid}': {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Reverts traverse ACEs on <paramref name="scriptsDirPath"/> and its ancestors for <paramref name="sid"/>.
    /// </summary>
    public void RevokeTraverseAccess(string sid, string scriptsDirPath)
    {
        try
        {
            var userIdentity = new SecurityIdentifier(sid);
            var syntheticEntry = new GrantedPathEntry { Path = scriptsDirPath, IsTraverseOnly = true };
            traverseGranter.RevertForPath(userIdentity, syntheticEntry, []);
        }
        catch (Exception ex)
        {
            log.Warn($"LogonScriptTraverseGranter: traverse revert failed for '{sid}': {ex.Message}");
        }
    }
}