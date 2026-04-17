using System.Security.AccessControl;
using RunFence.Acl.Permissions;
using RunFence.Acl.UI;

namespace RunFence.RunAs;

/// <summary>
/// Encapsulates the permission-prompt pattern used after RunAs account creation:
/// checks whether the new account needs access, collects grantable ancestors,
/// and shows the ancestor permission dialog.
/// </summary>
public class RunAsPermissionPromptHelper(IAclPermissionService aclPermission)
{
    /// <summary>
    /// Checks whether <paramref name="accountSid"/> needs access to <paramref name="filePath"/>
    /// and, if so, shows the ancestor permission dialog.
    /// </summary>
    /// <returns>
    /// The chosen <see cref="AncestorPermissionResult"/> (may be <c>null</c> if the user skips),
    /// or <c>null</c> if no grant is needed or no grantable ancestors exist.
    /// Throws <see cref="OperationCanceledException"/> if the user cancels the dialog.
    /// </returns>
    public AncestorPermissionResult? PromptIfNeeded(string filePath, string accountSid)
    {
        if (!aclPermission.NeedsPermissionGrantOrParent(filePath, accountSid))
            return null;

        var ancestors = aclPermission.GetGrantableAncestors(filePath);
        if (ancestors.Count == 0)
            return null;

        return AclPermissionDialogHelper.ShowAncestorPermissionDialog(
            null, "Missing permissions", ancestors, FileSystemRights.ReadAndExecute);
    }
}
