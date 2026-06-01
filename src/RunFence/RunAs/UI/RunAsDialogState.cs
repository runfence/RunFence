using System.Security.AccessControl;
using RunFence.Account.UI;
using RunFence.Account.UI.AppContainer;
using RunFence.Acl.Permissions;
using RunFence.Acl.UI;
using RunFence.Core.Models;
using RunFence.UI;

namespace RunFence.RunAs.UI;

/// <summary>
/// Holds the resolved selection state of a RunAs dialog and contains the logic to
/// populate it from the user's current list box selection.
/// </summary>
public class RunAsDialogState(
    string filePath,
    HashSet<string>? sidsNeedingPermission,
    IAclPermissionService aclPermission,
    IRunAsAncestorPermissionPrompter permissionPrompter)
{
    public CredentialEntry? SelectedCredential { get; private set; }
    public AppContainerEntry? SelectedContainer { get; private set; }
    public bool CreateNewAccountRequested { get; private set; }
    public bool CreateNewContainerRequested { get; private set; }
    public AncestorPermissionResult? PermissionGrant { get; private set; }

    /// <summary>
    /// Resolves the output state from the current list box selection.
    /// Returns false if the user cancelled a permission dialog (selection should be cleared).
    /// </summary>
    public bool ResolveSelectionState(
        IRunAsAccountOption? selectedOption,
        Form? dialogOwner,
        AppEntry? currentExistingApp,
        PrivilegeLevel selectedPrivilegeLevel,
        bool updateShortcutChecked,
        out PrivilegeLevel privilegeLevel,
        out bool updateOriginalShortcut,
        out AppEntry? existingAppForLaunch)
    {
        // Reset state
        SelectedCredential = null;
        SelectedContainer = null;
        CreateNewAccountRequested = false;
        CreateNewContainerRequested = false;
        PermissionGrant = null;

        privilegeLevel = selectedPrivilegeLevel;
        updateOriginalShortcut = updateShortcutChecked;
        existingAppForLaunch = currentExistingApp;

        switch (selectedOption)
        {
            case CreateAccountRunAsOption:
                CreateNewAccountRequested = true;
                PermissionGrant = null;
                break;
            case CreateContainerRunAsOption:
                CreateNewContainerRequested = true;
                break;
            case AppContainerRunAsOption containerOption:
            {
                SelectedContainer = containerOption.Container;

                if (!TryResolvePermissionGrant(containerOption.Sid, dialogOwner))
                {
                    SelectedContainer = null;
                    return false;
                }

                break;
            }
            case CredentialRunAsOption credentialOption:
            {
                SelectedCredential = credentialOption.Credential;

                if (!credentialOption.IsCurrentAccount && !TryResolvePermissionGrant(credentialOption.Sid, dialogOwner))
                {
                    SelectedCredential = null;
                    return false;
                }

                break;
            }
        }

        return true;
    }

    private bool TryResolvePermissionGrant(string sid, Form? owner)
    {
        if (sidsNeedingPermission?.Contains(sid) != true)
            return true;

        var ancestors = aclPermission.GetGrantableAncestors(filePath);
        if (ancestors.Count == 0)
            return true;

        try
        {
            PermissionGrant = permissionPrompter.Prompt(owner, ancestors);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
