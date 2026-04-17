using System.Security.AccessControl;
using RunFence.Acl;
using RunFence.Acl.Permissions;
using RunFence.Acl.QuickAccess;
using RunFence.Acl.UI;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Persistence;

namespace RunFence.RunAs.UI;

/// <summary>
/// Shows permission grant dialogs when an app entry is created or edited.
/// Handles both regular account apps and AppContainer apps.
/// </summary>
public class AppEntryPermissionPrompter(
    ILoggingService log,
    IAclPermissionService aclPermission,
    IPathGrantService pathGrantService,
    IDatabaseProvider databaseProvider,
    IQuickAccessPinService quickAccessPinService)
{
    /// <returns>true if any grant was applied and AccountGrants was updated (caller must save config).</returns>
    public bool PromptAndGrant(IWin32Window owner, AppEntry app)
    {
        if (app.IsFolder || app.IsUrlScheme || string.IsNullOrEmpty(app.ExePath))
            return false;

        if (app.AppContainerName != null)
            return PromptAndGrantContainerPermission(owner, app);

        if (string.IsNullOrEmpty(app.AccountSid))
            return false;

        if (!aclPermission.NeedsPermissionGrantOrParent(app.ExePath, app.AccountSid))
            return false;

        var ancestors = aclPermission.GetGrantableAncestors(app.ExePath);
        if (ancestors.Count == 0)
            return false;

        try
        {
            var selection = AclPermissionDialogHelper.ShowAncestorPermissionDialog(
                owner, "Missing permissions", ancestors, FileSystemRights.ReadAndExecute, "Save Without");
            if (selection == null)
                return false;

            // User already selected the path in the ancestor dialog — silent grant.
            // PathGrantService handles: ACE + AddGrant + traverse on ancestor directories.
            var grantResult = pathGrantService.EnsureAccess(app.AccountSid, selection.Path,
                selection.Rights, confirm: null);
            if (grantResult.GrantAdded)
                quickAccessPinService.PinFolders(app.AccountSid, [selection.Path]);
            return grantResult.DatabaseModified;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            log.Error("Failed to grant permission for app entry", ex);
        }

        return false;
    }

    private bool PromptAndGrantContainerPermission(IWin32Window owner, AppEntry app)
    {
        var database = databaseProvider.GetDatabase();
        var containerEntry = database.AppContainers.FirstOrDefault(c => c.Name == app.AppContainerName);
        if (containerEntry == null)
            return false;

        var directory = Path.GetDirectoryName(app.ExePath);
        if (string.IsNullOrEmpty(directory) || PathHelper.IsBlockedAclPath(directory))
            return false;

        var containerSid = containerEntry.Sid;
        if (string.IsNullOrEmpty(containerSid))
            return false;

        if (!aclPermission.NeedsPermissionGrant(directory, containerSid))
            return false;

        var ancestors = aclPermission.GetGrantableAncestors(app.ExePath);
        if (ancestors.Count == 0)
            return false;

        try
        {
            var selection = AclPermissionDialogHelper.ShowAncestorPermissionDialog(
                owner, "Missing permissions", ancestors, FileSystemRights.ReadAndExecute, "Save Without");
            if (selection == null)
                return false;

            // User already selected the path — silent grant.
            // PathGrantService handles: ACE + AddGrant + traverse + interactive user auto-grant
            // (for AppContainer SIDs, it automatically also grants the interactive desktop user).
            return pathGrantService.EnsureAccess(containerSid, selection.Path,
                selection.Rights, confirm: null).DatabaseModified;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            log.Error("Failed to grant permission for container app entry", ex);
            return false;
        }
    }
}