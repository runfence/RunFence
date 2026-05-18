using RunFence.Core.Models;
using RunFence.Persistence;

namespace RunFence.Apps.UI;

/// <summary>
/// Handles the apply/sync logic for saving an app entry from <see cref="Forms.AppEditDialog"/>.
/// Separates the multi-step async save flow (apply in-memory changes, invoke save, sync registry)
/// from the dialog's UI concerns.
/// </summary>
public class AppEditDialogSaveHandler(
    AppEditAssociationHandler associationHandler,
    IAppConfigService appConfigService)
{
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
        AppEntry result,
        string? selectedConfigPath,
        AppDatabase? database,
        IReadOnlyList<HandlerAssociationItem> currentAssociations,
        Func<Task> applyAsync)
    {
        IReadOnlyList<HandlerAssociationItem> originalAssociations = database != null
            ? associationHandler.GetCurrentAssociations(result.Id) ?? []
            : [];
        var originalConfigPath = database != null
            ? appConfigService.GetConfigPath(result.Id)
            : null;

        try
        {
            if (database != null)
            {
                appConfigService.AssignApp(result.Id, selectedConfigPath);
                associationHandler.ApplyChanges(result.Id, currentAssociations);
            }
        }
        catch (Exception ex)
        {
            if (database != null)
            {
                associationHandler.RevertChanges(result.Id, originalAssociations);
                appConfigService.AssignApp(result.Id, originalConfigPath);
            }
            return new AppEditSaveResult(AppEditSaveStatus.ValidationOrSystemFailed, ex.Message);
        }

        try
        {
            await applyAsync();
        }
        catch (OperationCanceledException ex)
        {
            if (database != null)
            {
                associationHandler.RevertChanges(result.Id, originalAssociations);
                appConfigService.AssignApp(result.Id, originalConfigPath);
            }
            return new AppEditSaveResult(AppEditSaveStatus.Canceled, ex.Message);
        }
        catch (Exception ex)
        {
            if (database != null)
            {
                associationHandler.RevertChanges(result.Id, originalAssociations);
                appConfigService.AssignApp(result.Id, originalConfigPath);
            }
            return new AppEditSaveResult(AppEditSaveStatus.SaveFailed, ex.Message);
        }

        if (database != null)
        {
            try
            {
                associationHandler.SyncRegistry();
            }
            catch (Exception ex)
            {
                return new AppEditSaveResult(
                    AppEditSaveStatus.SavedWithRegistryWarning,
                    RegistrySyncWarning: ex.Message);
            }
        }

        return new AppEditSaveResult(AppEditSaveStatus.Saved);
    }
}
