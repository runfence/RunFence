using System.Security.AccessControl;
using RunFence.Acl;
using RunFence.Acl.Permissions;
using RunFence.Acl.QuickAccess;
using RunFence.Acl.UI;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Persistence;

namespace RunFence.RunAs.UI;

/// <summary>
/// Collects app-entry permission grant decisions and applies the selected grant after save.
/// Handles both regular account apps and AppContainer apps.
/// </summary>
public class AppEntryPermissionPrompter(
    ILoggingService log,
    IAclPermissionService aclPermission,
    IGrantMutatorService grantMutatorService,
    IDatabaseProvider databaseProvider,
    IQuickAccessPinService quickAccessPinService)
{
    public AppEntryPermissionPromptDecision PromptForGrant(IWin32Window owner, AppEntry app)
    {
        if (app.IsFolder || app.IsUrlScheme || string.IsNullOrEmpty(app.ExePath))
            return new AppEntryPermissionPromptDecision(AppEntryPermissionPromptResult.SaveWithoutGrant);

        if (app.AppContainerName != null)
            return PromptForContainerGrant(owner, app);

        if (string.IsNullOrEmpty(app.AccountSid))
            return new AppEntryPermissionPromptDecision(AppEntryPermissionPromptResult.SaveWithoutGrant);

        if (!aclPermission.NeedsPermissionGrantOrParent(app.ExePath, app.AccountSid))
            return new AppEntryPermissionPromptDecision(AppEntryPermissionPromptResult.SaveWithoutGrant);

        var ancestors = aclPermission.GetGrantableAncestors(app.ExePath);
        if (ancestors.Count == 0)
            return new AppEntryPermissionPromptDecision(AppEntryPermissionPromptResult.SaveWithoutGrant);

        try
        {
            var selection = AclPermissionDialogHelper.ShowAncestorPermissionDialog(
                owner, "Missing permissions", ancestors, FileSystemRights.ReadAndExecute, "Save Without");
            if (selection == null)
                return new AppEntryPermissionPromptDecision(AppEntryPermissionPromptResult.SaveWithoutGrant);

            return new AppEntryPermissionPromptDecision(
                AppEntryPermissionPromptResult.GrantRequested,
                new AppEntryPermissionGrantRequest(
                    app.AccountSid,
                    selection.Path,
                    selection.Rights,
                    PinFolderAfterGrant: true));
        }
        catch (OperationCanceledException)
        {
            return new AppEntryPermissionPromptDecision(AppEntryPermissionPromptResult.Canceled);
        }
        catch (Exception ex)
        {
            log.Error("Failed to prepare permission grant for app entry", ex);
        }

        return new AppEntryPermissionPromptDecision(AppEntryPermissionPromptResult.SaveWithoutGrant);
    }

    public string? TryApplyGrant(AppEntryPermissionGrantRequest grantRequest)
    {
        try
        {
            var grantResult = grantMutatorService.EnsureAccess(
                grantRequest.TargetSid,
                grantRequest.Path,
                grantRequest.Rights,
                confirm: null);
            if (grantRequest.PinFolderAfterGrant && grantResult.GrantApplied)
                quickAccessPinService.PinFolders(grantRequest.TargetSid, [grantRequest.Path]);
            return null;
        }
        catch (GrantOperationException ex)
        {
            log.Error("Failed to apply selected permission grant for app entry", ex);
            return FormatGrantFailure(ex, grantRequest.Path);
        }
        catch (Exception ex)
        {
            log.Error("Failed to apply selected permission grant for app entry", ex);
            return ex.Message;
        }
    }

    private AppEntryPermissionPromptDecision PromptForContainerGrant(IWin32Window owner, AppEntry app)
    {
        var database = databaseProvider.GetDatabase();
        var containerEntry = database.AppContainers.FirstOrDefault(c => c.Name == app.AppContainerName);
        if (containerEntry == null)
            return new AppEntryPermissionPromptDecision(AppEntryPermissionPromptResult.SaveWithoutGrant);

        var directory = Path.GetDirectoryName(app.ExePath);
        if (string.IsNullOrEmpty(directory) || PathHelper.IsBlockedAclPath(directory))
            return new AppEntryPermissionPromptDecision(AppEntryPermissionPromptResult.SaveWithoutGrant);

        var containerSid = containerEntry.Sid;
        if (string.IsNullOrEmpty(containerSid))
            return new AppEntryPermissionPromptDecision(AppEntryPermissionPromptResult.SaveWithoutGrant);

        if (!aclPermission.NeedsPermissionGrant(directory, containerSid))
            return new AppEntryPermissionPromptDecision(AppEntryPermissionPromptResult.SaveWithoutGrant);

        var ancestors = aclPermission.GetGrantableAncestors(app.ExePath);
        if (ancestors.Count == 0)
            return new AppEntryPermissionPromptDecision(AppEntryPermissionPromptResult.SaveWithoutGrant);

        try
        {
            var selection = AclPermissionDialogHelper.ShowAncestorPermissionDialog(
                owner, "Missing permissions", ancestors, FileSystemRights.ReadAndExecute, "Save Without");
            if (selection == null)
                return new AppEntryPermissionPromptDecision(AppEntryPermissionPromptResult.SaveWithoutGrant);

            return new AppEntryPermissionPromptDecision(
                AppEntryPermissionPromptResult.GrantRequested,
                new AppEntryPermissionGrantRequest(
                    containerSid,
                    selection.Path,
                    selection.Rights,
                    PinFolderAfterGrant: false));
        }
        catch (OperationCanceledException)
        {
            return new AppEntryPermissionPromptDecision(AppEntryPermissionPromptResult.Canceled);
        }
        catch (Exception ex)
        {
            log.Error("Failed to prepare permission grant for container app entry", ex);
            return new AppEntryPermissionPromptDecision(AppEntryPermissionPromptResult.SaveWithoutGrant);
        }
    }

    private static string FormatGrantFailure(GrantOperationException ex, string path)
        => GrantApplyFailureFormatter.IsSaveFailureStep(ex.Step)
            ? $"RunFence could not save the permission grant for '{path}': {ex.Cause.Message}"
            : $"RunFence saved the permission grant for '{path}', but applying filesystem access failed: {ex.Cause.Message}";
}
