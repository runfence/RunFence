using RunFence.Apps.UI.Forms;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Persistence;

namespace RunFence.Apps.UI;

/// <summary>
/// Handles handler registration synchronization for the ApplicationsPanel:
/// cleaning up orphaned mappings when apps are removed, syncing handler registrations
/// after associations or edits change, and opening the associations management dialog.
/// </summary>
public class ApplicationsHandlerSyncHelper(
    IAppHandlerRegistrationService handlerRegistrationService,
    IHandlerMappingService handlerMappingService,
    ILoggingService log,
    IDatabaseProvider databaseProvider)
{
    public Dictionary<string, string> GetEffectiveMappings()
    {
        var database = databaseProvider.GetDatabase();
        return handlerMappingService.GetEffectiveHandlerMappings(database);
    }

    /// <summary>
    /// Opens the handler associations management dialog.
    /// </summary>
    public void OpenAssociationsDialog(IWin32Window owner, Func<AppDatabase> getDatabase, Action saveDatabase)
    {
        using var dlg = new HandlerMappingsDialog(handlerMappingService, handlerRegistrationService,
            log, getDatabase, saveDatabase);
        dlg.ShowDialog(owner);
    }

    /// <summary>
    /// Removes handler mappings for any app IDs that no longer exist in the database,
    /// then syncs registrations if any were removed.
    /// </summary>
    /// <returns>True if any mappings were removed and registrations were re-synced.</returns>
    public bool CleanupOrphanedMappings()
    {
        var database = databaseProvider.GetDatabase();
        var existingAppIds = new HashSet<string>(database.Apps.Select(a => a.Id));
        var effectiveMappings = handlerMappingService.GetEffectiveHandlerMappings(database);
        bool changed = false;
        foreach (var kvp in effectiveMappings.ToList())
        {
            if (!existingAppIds.Contains(kvp.Value))
            {
                handlerMappingService.RemoveHandlerMapping(kvp.Key, database);
                changed = true;
            }
        }

        if (changed)
        {
            var updatedMappings = handlerMappingService.GetEffectiveHandlerMappings(database);
            handlerRegistrationService.Sync(updatedMappings, database.Apps);
        }

        return changed;
    }
}