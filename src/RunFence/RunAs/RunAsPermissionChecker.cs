using RunFence.Acl.Permissions;
using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.RunAs;

/// <summary>
/// Pre-computes which accounts and AppContainer SIDs need permission grants for a given file path.
/// This computation runs outside the secure desktop so it can access the file system and
/// COM services without the restrictions of the secure desktop session.
/// </summary>
public class RunAsPermissionChecker(IAclPermissionService aclPermission)
{
    /// <summary>
    /// Returns the set of SIDs (user accounts + AppContainer SIDs) that need permission grants
    /// for the given file path, or null if the path is a blocked ACL root (cannot be granted).
    /// </summary>
    public HashSet<string>? ComputeSidsNeedingPermission(
        string filePath,
        IReadOnlyList<CredentialEntry> credentials,
        IReadOnlyList<AppContainerEntry> appContainers)
    {
        // Use IsBlockedAclRoot (not IsBlockedAclPath) — children of blocked roots are safe for allow ACEs
        if (PathHelper.IsBlockedAclRoot(filePath))
            return null;

        var sidsNeedingPermission = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var cred in credentials)
        {
            if (!cred.IsCurrentAccount && aclPermission.NeedsPermissionGrantOrParent(filePath, cred.Sid))
                sidsNeedingPermission.Add(cred.Sid);
        }

        // Also check AppContainer SIDs — containers need explicit ACEs just like user accounts.
        // For the dual access check, the interactive user must also have access (step 1),
        // so check both the container SID and the interactive user SID.
        var interactiveSid = NativeTokenHelper.TryGetInteractiveUserSid()?.Value;
        var isCrossUser = interactiveSid != null &&
                          !string.Equals(interactiveSid, SidResolutionHelper.GetCurrentUserSid(), StringComparison.OrdinalIgnoreCase);

        foreach (var container in appContainers)
        {
            var containerSid = container.Sid;
            if (string.IsNullOrEmpty(containerSid))
                continue;
            if (aclPermission.NeedsPermissionGrantOrParent(filePath, containerSid) ||
                (isCrossUser && aclPermission.NeedsPermissionGrantOrParent(filePath, interactiveSid!)))
                sidsNeedingPermission.Add(containerSid);
        }

        return sidsNeedingPermission;
    }
}