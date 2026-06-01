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
    AppEntryEnforcementCoordinator enforcementCoordinator,
    ILoggingService log)
{
    public ApplicationsCrudOperationResult ApplyChanges(
        IApplicationMutationContext context,
        AppEntry app,
        ShortcutTraversalCache shortcutCache,
        AppEntryChangeSet changeSet,
        string? selectAppId = null,
        int fallbackIndex = -1)
    {
        var saveResult = SaveAfterMutation(context, app, changeSet.ConfigSaveScope, selectAppId, fallbackIndex);
        if (saveResult.Status == ApplicationsCrudOperationStatus.SaveFailed)
            return saveResult;

        if (!AppEntryEnforcementCoordinator.RequiresEnforcement(changeSet))
            return saveResult;

        var warning = ApplyEnforcement(context, app, shortcutCache, changeSet);
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
        AppEntryChangeSet changeSet,
        ShortcutWarningPolicy shortcutWarningPolicy = ShortcutWarningPolicy.TreatAsFailure)
    {
        if (!AppEntryEnforcementCoordinator.RequiresEnforcement(changeSet))
            return new ApplicationsCrudOperationResult(ApplicationsCrudOperationStatus.Succeeded);

        string? warning = null;
        string? error = null;
        try
        {
            enforcementCoordinator.RevertTargetedChanges(app, context.Database.Apps, shortcutCache, changeSet);
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
            if (changeSet.RequiresAclReapply)
            {
                var appsAfterRevert = context.Database.Apps.Where(a => a.Id != app.Id).ToList();
                aclService.RecomputeAllAncestorAcls(appsAfterRevert);
            }
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
        AppEditConfigSaveScope configSaveScope,
        string? selectAppId = null,
        int fallbackIndex = -1)
    {
        try
        {
            context.SaveAndRefresh(
                selectAppId,
                fallbackIndex,
                targetedSave: configSaveScope == AppEditConfigSaveScope.CurrentAppConfigOnly);
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
        ShortcutTraversalCache shortcutCache,
        AppEntryChangeSet changeSet)
    {
        if (!AppEntryEnforcementCoordinator.RequiresEnforcement(changeSet))
            return new ApplicationsCrudOperationResult(ApplicationsCrudOperationStatus.Succeeded);

        string? warning = null;

        try
        {
            enforcementCoordinator.ApplyTargetedChanges(previousApp, allAppsAfterRollback, shortcutCache, changeSet);
        }
        catch (Exception ex)
        {
            log.Error($"Failed to restore previous enforcement for '{previousApp.Name}'", ex);
            warning = ex.Message;
        }

        try
        {
            if (changeSet.RequiresAclReapply)
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
        ShortcutTraversalCache shortcutCache,
        AppEntryChangeSet changeSet)
    {
        string? warning = null;
        try
        {
            enforcementCoordinator.ApplyTargetedChanges(app, context.Database.Apps, shortcutCache, changeSet);
        }
        catch (Exception ex)
        {
            log.Error($"Failed to apply changes for {app.Name}", ex);
            warning = ex.Message;
        }

        try
        {
            if (changeSet.RequiresAclReapply)
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
