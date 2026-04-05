using System.Security.AccessControl;
using RunFence.Acl.Permissions;
using RunFence.Acl.QuickAccess;
using RunFence.Acl.UI;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Launch.Container;
using RunFence.Persistence;

namespace RunFence.RunAs.UI;

/// <summary>
/// Shows permission grant dialogs when an app entry is created or edited.
/// Handles both regular account apps and AppContainer apps.
/// </summary>
public class AppEntryPermissionPrompter
{
    private readonly IAppContainerService _appContainerService;
    private readonly ILoggingService _log;
    private readonly IAclPermissionService _aclPermission;
    private readonly IPermissionGrantService _permissionGrantService;
    private readonly IDatabaseProvider _databaseProvider;
    private readonly IQuickAccessPinService _quickAccessPinService;

    public AppEntryPermissionPrompter(
        IAppContainerService appContainerService,
        ILoggingService log,
        IAclPermissionService aclPermission,
        IPermissionGrantService permissionGrantService,
        IDatabaseProvider databaseProvider,
        IQuickAccessPinService quickAccessPinService)
    {
        _appContainerService = appContainerService;
        _log = log;
        _aclPermission = aclPermission;
        _permissionGrantService = permissionGrantService;
        _databaseProvider = databaseProvider;
        _quickAccessPinService = quickAccessPinService;
    }

    /// <returns>true if any grant was applied and AccountGrants was updated (caller must save config).</returns>
    public bool PromptAndGrant(IWin32Window owner, AppEntry app)
    {
        if (app.IsFolder || app.IsUrlScheme || string.IsNullOrEmpty(app.ExePath))
            return false;

        if (app.AppContainerName != null)
            return PromptAndGrantContainerPermission(owner, app);

        if (string.IsNullOrEmpty(app.AccountSid))
            return false;

        if (!_aclPermission.NeedsPermissionGrantOrParent(app.ExePath, app.AccountSid))
            return false;

        var ancestors = _aclPermission.GetGrantableAncestors(app.ExePath);
        if (ancestors.Count == 0)
            return false;

        try
        {
            var selection = AclPermissionDialogHelper.ShowAncestorPermissionDialog(
                owner, "Missing permissions", ancestors, FileSystemRights.ReadAndExecute, "Save Without");
            if (selection == null)
                return false;

            // User already selected the path in the ancestor dialog — silent grant.
            // PermissionGrantService handles: ACE + AddGrant + traverse on ancestor directories.
            var grantResult = _permissionGrantService.EnsureAccess(selection.Path, app.AccountSid,
                selection.Rights, confirm: null);
            if (grantResult.GrantAdded)
                _quickAccessPinService.PinFolders(app.AccountSid, [selection.Path]);
            return grantResult.DatabaseModified;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _log.Error("Failed to grant permission for app entry", ex);
        }

        return false;
    }

    private bool PromptAndGrantContainerPermission(IWin32Window owner, AppEntry app)
    {
        var database = _databaseProvider.GetDatabase();
        var containerEntry = database.AppContainers.FirstOrDefault(c => c.Name == app.AppContainerName);
        if (containerEntry == null)
            return false;

        var directory = Path.GetDirectoryName(app.ExePath);
        if (string.IsNullOrEmpty(directory) || PathHelper.IsBlockedAclPath(directory))
            return false;

        var containerSid = _appContainerService.GetSid(app.AppContainerName!);

        if (!_aclPermission.NeedsPermissionGrant(directory, containerSid))
            return false;

        var ancestors = _aclPermission.GetGrantableAncestors(app.ExePath);
        if (ancestors.Count == 0)
            return false;

        try
        {
            var selection = AclPermissionDialogHelper.ShowAncestorPermissionDialog(
                owner, "Missing permissions", ancestors, FileSystemRights.ReadAndExecute, "Save Without");
            if (selection == null)
                return false;

            // User already selected the path — silent grant.
            // PermissionGrantService handles: ACE + AddGrant + traverse + interactive user auto-grant
            // (for AppContainer SIDs, it automatically also grants the interactive desktop user).
            return _permissionGrantService.EnsureAccess(selection.Path, containerSid,
                selection.Rights, confirm: null).DatabaseModified;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            _log.Error("Failed to grant permission for container app entry", ex);
            return false;
        }
    }
}