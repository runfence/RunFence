using RunFence.Core.Models;
using RunFence.Core;
using RunFence.Persistence;

namespace RunFence.Apps.UI;

/// <summary>
/// Handles the apply/sync logic for saving an app entry from <see cref="Forms.AppEditDialog"/>.
/// Separates the multi-step async save flow (apply in-memory changes, invoke save, sync registry)
/// from the dialog's UI concerns.
/// </summary>
public class AppEditDialogSaveHandler(
    AppEditAssociationHandler associationHandler,
    IAppConfigService appConfigService,
    ILoggingService log)
{
    private const string FileToFolderOrUrlAssociationMessage =
        "Remove this application's handler associations before changing it to a folder or URL app.";

    /// <summary>
    /// Executes the full save flow when an external apply action is provided:
    /// <list type="number">
    ///   <item>Applies pre-save in-memory changes: config assignment and handler mappings.</item>
    ///   <item>Invokes <paramref name="applyAsync"/> (the external apply action).</item>
    ///   <item>On success: syncs registry handler registrations.</item>
    ///   <item>On pre-save mutation failure: restores the original in-memory state.</item>
    /// </list>
    /// Returns a status-bearing result that distinguishes cancel, pre-save mutation failures,
    /// save failures, and post-save registry-sync warnings.
    /// </summary>
    public async Task<AppEditSaveResult> TrySaveAndApply(
        AppDatabase? database,
        AppEditDialogApplyContext applyContext,
        Func<AppEditDialogApplyContext, Task> applyAsync)
    {
        var result = applyContext.Result;
        var changeSet = applyContext.ChangeSet;
        var previousApp = applyContext.PreviousApp;
        var previousConfigPath = applyContext.PreviousConfigPath;
        var newConfigPath = applyContext.SelectedConfigPath;
        var currentAssociations = applyContext.CurrentAssociations;
        IReadOnlyList<HandlerAssociationItem> originalAssociations = database != null
            ? associationHandler.GetCurrentAssociations(result.Id) ?? []
            : [];

        if (previousApp != null &&
            !previousApp.IsFolder &&
            !previousApp.IsUrlScheme &&
            (result.IsFolder || result.IsUrlScheme) &&
            currentAssociations.Count > 0)
        {
            return new AppEditSaveResult(
                AppEditSaveStatus.ValidationOrSystemFailed,
                FileToFolderOrUrlAssociationMessage);
        }

        try
        {
            if (database != null)
            {
                appConfigService.AssignApp(result.Id, newConfigPath);
                if (changeSet.RequiresHandlerSync)
                    associationHandler.ApplyChanges(result.Id, currentAssociations);
            }
        }
        catch (Exception ex)
        {
            log.Error($"Failed to prepare app edit save for '{result.Name}' ({result.Id})", ex);
            if (database != null)
            {
                if (changeSet.RequiresHandlerSync)
                    associationHandler.RevertChanges(result.Id, originalAssociations);
                appConfigService.AssignApp(result.Id, previousConfigPath);
            }
            return new AppEditSaveResult(AppEditSaveStatus.ValidationOrSystemFailed, ex.Message);
        }

        try
        {
            await applyAsync(applyContext);
        }
        catch (OperationCanceledException ex)
        {
            if (database != null)
            {
                if (changeSet.RequiresHandlerSync)
                    associationHandler.RevertChanges(result.Id, originalAssociations);
                appConfigService.AssignApp(result.Id, previousConfigPath);
            }
            return new AppEditSaveResult(AppEditSaveStatus.Canceled, ex.Message);
        }
        catch (Exception ex)
        {
            log.Error($"Failed to apply app edit save for '{result.Name}' ({result.Id})", ex);
            if (database != null)
            {
                if (changeSet.RequiresHandlerSync)
                    associationHandler.RevertChanges(result.Id, originalAssociations);
                appConfigService.AssignApp(result.Id, previousConfigPath);
            }
            return new AppEditSaveResult(AppEditSaveStatus.SaveFailed, ex.Message);
        }

        if (database != null && changeSet.RequiresHandlerSync)
        {
            try
            {
                associationHandler.SyncRegistry();
            }
            catch (Exception ex)
            {
                log.Error($"Failed to sync handler registry after saving app '{result.Name}' ({result.Id})", ex);
                return new AppEditSaveResult(
                    AppEditSaveStatus.SavedWithRegistryWarning,
                    RegistrySyncWarning: ex.Message);
            }
        }

        return new AppEditSaveResult(AppEditSaveStatus.Saved);
    }
}
