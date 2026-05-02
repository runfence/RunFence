using RunFence.Core.Models;
using RunFence.Persistence;

namespace RunFence.Apps.UI;

/// <summary>
/// Handles the apply/sync/rollback logic for saving an app entry from <see cref="Forms.AppEditDialog"/>.
/// Separates the multi-step async save flow (apply in-memory changes, invoke save, sync registry,
/// roll back on failure) from the dialog's UI concerns.
/// </summary>
public class AppEditDialogSaveHandler(
    AppEditAssociationHandler associationHandler,
    IAppConfigService appConfigService)
{
    /// <summary>
    /// Executes the full save flow when an external save action (ApplyRequested) is provided:
    /// <list type="number">
    ///   <item>Pre-assigns app to config.</item>
    ///   <item>Applies in-memory handler mapping changes from <paramref name="currentAssociations"/>.</item>
    ///   <item>Invokes <paramref name="applyRequested"/> (the external save action).</item>
    ///   <item>On success: syncs registry handler registrations.</item>
    ///   <item>On failure: rolls back in-memory changes and reports error via <paramref name="reportError"/>.</item>
    /// </list>
    /// Returns <c>true</c> when the save succeeded (including partial success where registry sync failed).
    /// Returns <c>false</c> when the save itself failed and changes were rolled back.
    /// </summary>
    public async Task<bool> TrySaveAndApply(
        AppEntry result,
        string? selectedConfigPath,
        AppDatabase? database,
        IReadOnlyList<HandlerAssociationItem> currentAssociations,
        Func<Task> applyRequested,
        Action<string> reportError,
        Action<string> reportWarning)
    {
        if (database != null)
            appConfigService.AssignApp(result.Id, selectedConfigPath);

        IReadOnlyList<HandlerAssociationItem> originalAssociations = database != null
            ? associationHandler.GetCurrentAssociations(result.Id) ?? []
            : [];

        try
        {
            if (database != null)
                associationHandler.ApplyChanges(result.Id, currentAssociations);
            await applyRequested();
        }
        catch (Exception ex)
        {
            if (database != null)
                associationHandler.RevertChanges(result.Id, originalAssociations);
            reportError(ex.Message);
            return false;
        }

        if (database != null)
        {
            try
            {
                associationHandler.SyncRegistry();
            }
            catch (Exception ex)
            {
                reportWarning(ex.Message);
            }
        }

        return true;
    }
}
