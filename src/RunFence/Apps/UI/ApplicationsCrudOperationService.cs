using RunFence.Acl;
using RunFence.Apps.Shortcuts;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;

namespace RunFence.Apps.UI;

/// <summary>
/// Applies and reverts persisted application CRUD mutations around save and enforcement steps.
/// Separates mutation/save/enforcement behavior from dialog orchestration.
/// </summary>
public class ApplicationsCrudOperationService(
    IAclService aclService,
    AppEntryEnforcementHelper enforcementHelper,
    ILoggingService log)
{
    public ApplicationsCrudOperationResult ApplyChanges(
        IApplicationMutationContext context,
        AppEntry app,
        ShortcutTraversalCache shortcutCache,
        string? selectAppId = null,
        int fallbackIndex = -1,
        bool targetedSave = false)
    {
        var saveResult = SaveAfterMutation(context, app, selectAppId, fallbackIndex, targetedSave);
        if (saveResult.Status == ApplicationsCrudOperationStatus.SaveFailed)
            return saveResult;

        var warning = ApplyEnforcement(context, app, shortcutCache);
        return warning == null
            ? new ApplicationsCrudOperationResult(ApplicationsCrudOperationStatus.Succeeded)
            : new ApplicationsCrudOperationResult(
                ApplicationsCrudOperationStatus.SucceededWithEnforcementWarning,
                WarningMessage: warning);
    }

    public ApplicationsCrudOperationResult RevertChanges(
        IApplicationMutationContext context,
        AppEntry app,
        ShortcutTraversalCache shortcutCache,
        ShortcutWarningPolicy shortcutWarningPolicy = ShortcutWarningPolicy.TreatAsFailure)
    {
        string? warning = null;
        string? error = null;
        try
        {
            enforcementHelper.RevertChanges(app, context.Database.Apps, shortcutCache);
        }
        catch (ShortcutEnforcementException ex)
        {
            if (shortcutWarningPolicy == ShortcutWarningPolicy.DemoteToWarning)
            {
                log.Warn($"Failed to revert shortcut changes for {app.Name}: {ex.Message}");
                warning = ex.Message;
            }
            else
            {
                log.Error($"Failed to revert changes for {app.Name}", ex);
                error = ex.Message;
            }
        }
        catch (Exception ex)
        {
            log.Error($"Failed to revert changes for {app.Name}", ex);
            error = ex.Message;
        }

        try
        {
            var appsAfterRevert = context.Database.Apps.Where(a => a.Id != app.Id).ToList();
            aclService.RecomputeAllAncestorAcls(appsAfterRevert);
        }
        catch (Exception ex)
        {
            log.Error($"Failed to recompute ancestor ACLs after revert for {app.Name}", ex);
            error = error == null ? ex.Message : $"{error}; ancestor ACL recompute failed: {ex.Message}";
        }

        if (error != null)
        {
            return new ApplicationsCrudOperationResult(
                ApplicationsCrudOperationStatus.EnforcementFailed,
                ErrorMessage: warning == null ? error : $"{warning}\n\n{error}");
        }

        return warning == null
            ? new ApplicationsCrudOperationResult(ApplicationsCrudOperationStatus.Succeeded)
            : new ApplicationsCrudOperationResult(
                ApplicationsCrudOperationStatus.SucceededWithEnforcementWarning,
                WarningMessage: warning);
    }

    public ApplicationsCrudOperationResult SaveAfterMutation(
        IApplicationMutationContext context,
        AppEntry app,
        string? selectAppId = null,
        int fallbackIndex = -1,
        bool targetedSave = false)
    {
        try
        {
            context.SaveAndRefresh(selectAppId, fallbackIndex, targetedSave);
        }
        catch (Exception ex)
        {
            log.Error($"Failed to save application '{app.Name}'", ex);
            return new ApplicationsCrudOperationResult(
                ApplicationsCrudOperationStatus.SaveFailed,
                ErrorMessage: ex.Message);
        }

        return new ApplicationsCrudOperationResult(ApplicationsCrudOperationStatus.Succeeded);
    }

    public ApplicationsCrudOperationResult RestoreEnforcementAfterFailedEdit(
        AppEntry previousApp,
        IReadOnlyList<AppEntry> allAppsAfterRollback,
        ShortcutTraversalCache shortcutCache)
    {
        string? warning = null;

        try
        {
            enforcementHelper.ApplyChanges(previousApp, allAppsAfterRollback, shortcutCache);
        }
        catch (Exception ex)
        {
            log.Error($"Failed to restore previous enforcement for '{previousApp.Name}'", ex);
            warning = ex.Message;
        }

        try
        {
            aclService.RecomputeAllAncestorAcls(allAppsAfterRollback);
        }
        catch (Exception ex)
        {
            log.Error($"Failed to recompute ancestor ACLs while restoring previous app '{previousApp.Name}'", ex);
            warning = warning == null ? ex.Message : $"{warning}; ancestor ACL recompute failed: {ex.Message}";
        }

        return warning == null
            ? new ApplicationsCrudOperationResult(ApplicationsCrudOperationStatus.Succeeded)
            : new ApplicationsCrudOperationResult(
                ApplicationsCrudOperationStatus.SucceededWithEnforcementWarning,
                WarningMessage: warning);
    }

    private string? ApplyEnforcement(
        IApplicationMutationContext context,
        AppEntry app,
        ShortcutTraversalCache shortcutCache)
    {
        string? warning = null;
        try
        {
            enforcementHelper.ApplyChanges(app, context.Database.Apps, shortcutCache);
        }
        catch (Exception ex)
        {
            log.Error($"Failed to apply changes for {app.Name}", ex);
            warning = ex.Message;
        }

        try
        {
            aclService.RecomputeAllAncestorAcls(context.Database.Apps);
        }
        catch (Exception ex)
        {
            log.Error($"Failed to recompute ancestor ACLs for {app.Name}", ex);
            warning = warning == null ? ex.Message : $"{warning}; ancestor ACL recompute failed: {ex.Message}";
        }

        return warning;
    }
}
