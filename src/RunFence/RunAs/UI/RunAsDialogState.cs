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
public class RunAsDialogState
{
    public CredentialEntry? SelectedCredential { get; private set; }
    public AppContainerEntry? SelectedContainer { get; private set; }
    public bool CreateNewAccountRequested { get; private set; }
    public bool CreateNewContainerRequested { get; private set; }
    public AncestorPermissionResult? PermissionGrant { get; private set; }

    private readonly string _filePath;
    private readonly HashSet<string>? _sidsNeedingPermission;
    private readonly IAclPermissionService _aclPermission;

    public RunAsDialogState(
        string filePath,
        HashSet<string>? sidsNeedingPermission,
        IAclPermissionService aclPermission)
    {
        _filePath = filePath;
        _sidsNeedingPermission = sidsNeedingPermission;
        _aclPermission = aclPermission;
    }

    /// <summary>
    /// Resolves the output state from the current list box selection.
    /// Returns false if the user cancelled a permission dialog (selection should be cleared).
    /// </summary>
    public bool ResolveSelectionState(
        object? selectedItem,
        Form? dialogOwner,
        AppEntry? currentExistingApp,
        bool lowIntegrityChecked,
        bool splitTokenChecked,
        bool updateShortcutChecked,
        out bool launchAsLowIntegrity,
        out bool launchAsSplitToken,
        out bool updateOriginalShortcut,
        out AppEntry? existingAppForLaunch)
    {
        // Reset state
        SelectedCredential = null;
        SelectedContainer = null;
        CreateNewAccountRequested = false;
        CreateNewContainerRequested = false;
        PermissionGrant = null;

        launchAsLowIntegrity = lowIntegrityChecked;
        launchAsSplitToken = splitTokenChecked;
        updateOriginalShortcut = updateShortcutChecked;
        existingAppForLaunch = currentExistingApp;

        switch (selectedItem)
        {
            case CreateAccountItem:
                CreateNewAccountRequested = true;
                PermissionGrant = null;
                break;
            case CreateContainerItem:
                CreateNewContainerRequested = true;
                break;
            case AppContainerDisplayItem containerItem:
            {
                SelectedContainer = containerItem.Container;

                if (!TryResolvePermissionGrant(containerItem.ContainerSid, dialogOwner))
                {
                    SelectedContainer = null;
                    return false;
                }

                break;
            }
            case CredentialDisplayItem item:
            {
                SelectedCredential = item.Credential;

                if (!item.Credential.IsCurrentAccount && !TryResolvePermissionGrant(item.Credential.Sid, dialogOwner))
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
        if (_sidsNeedingPermission?.Contains(sid) != true)
            return true;

        var ancestors = _aclPermission.GetGrantableAncestors(_filePath);
        if (ancestors.Count == 0)
            return true;

        try
        {
            PermissionGrant = AclPermissionDialogHelper.ShowAncestorPermissionDialog(
                owner, "Missing permissions", ancestors,
                FileSystemRights.ReadAndExecute);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}