using RunFence.Account;
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
    IAssociationAutoSetService autoSetService,
    ISidNameCacheService sidNameCache,
    IDatabaseProvider databaseProvider,
    Func<HandlerMappingsDialog> handlerMappingsDialogFactory)
{
    /// <summary>
    /// Returns all handler mappings across all loaded configs (key → all entries per key).
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<HandlerMappingEntry>> GetAllMappings()
    {
        var database = databaseProvider.GetDatabase();
        return handlerMappingService.GetAllHandlerMappings(database);
    }

    /// <summary>
    /// Opens the handler associations management dialog.
    /// </summary>
    public void OpenAssociationsDialog(IWin32Window owner, Action saveDatabase)
    {
        var interactiveSid = SidResolutionHelper.GetInteractiveUserSid();
        var interactiveUsername = interactiveSid != null ? sidNameCache.GetDisplayName(interactiveSid) ?? "User" : "User";

        using var dlg = handlerMappingsDialogFactory();
        dlg.Initialize(() => databaseProvider.GetDatabase(), saveDatabase, interactiveUsername);
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
        var allMappings = handlerMappingService.GetAllHandlerMappings(database);
        bool changed = false;
        var removedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in allMappings)
        {
            foreach (var entry in kvp.Value)
            {
                if (!existingAppIds.Contains(entry.AppId))
                {
                    handlerMappingService.RemoveHandlerMapping(kvp.Key, entry.AppId, database);
                    removedKeys.Add(kvp.Key);
                    changed = true;
                }
            }
        }

        if (changed)
        {
            var remainingMappings = handlerMappingService.GetAllHandlerMappings(database);
            foreach (var key in removedKeys)
            {
                if (!remainingMappings.ContainsKey(key))
                    autoSetService.RestoreKeyForAllUsers(key);
            }

            var updatedMappings = handlerMappingService.GetEffectiveHandlerMappings(database);
            handlerRegistrationService.Sync(updatedMappings, database.Apps);
            autoSetService.AutoSetForAllUsers();
        }

        return changed;
    }
}