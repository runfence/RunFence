using RunFence.Apps.UI.Forms;
using RunFence.Persistence;

namespace RunFence.Apps.UI;

/// <summary>
/// Manages handler association state for <see cref="AppEditDialog"/>:
/// populates the associations section from the database, applies in-memory mapping changes,
/// and syncs registry registrations after a successful save.
/// </summary>
public class AppEditAssociationHandler(
    IHandlerMappingService handlerMappingService,
    IAppHandlerRegistrationService handlerRegistrationService,
    IDatabaseProvider databaseProvider)
{
    /// <summary>
    /// Populates the associations section with keys currently mapped to <paramref name="appId"/>
    /// in the effective handler mappings.
    /// </summary>
    public List<string>? GetCurrentAssociations(string appId)
    {
        var database = databaseProvider.GetDatabase();
        var effectiveMappings = handlerMappingService.GetEffectiveHandlerMappings(database);
        var associations = effectiveMappings
            .Where(kv => string.Equals(kv.Value, appId, StringComparison.OrdinalIgnoreCase))
            .Select(kv => kv.Key)
            .ToList();

        return associations.Count > 0 ? associations : null;
    }

    /// <summary>
    /// Applies in-memory handler mapping changes (adds/removes) to the database
    /// based on the diff between current and new associations.
    /// Must be called before saving so the correct config file is written.
    /// </summary>
    public void ApplyChanges(string appId, List<string> newAssociations)
    {
        var database = databaseProvider.GetDatabase();
        var effectiveMappings = handlerMappingService.GetEffectiveHandlerMappings(database);
        var originalKeys = effectiveMappings
            .Where(kv => string.Equals(kv.Value, appId, StringComparison.OrdinalIgnoreCase))
            .Select(kv => kv.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var key in originalKeys)
        {
            if (!newAssociations.Any(a => string.Equals(a, key, StringComparison.OrdinalIgnoreCase)))
                handlerMappingService.RemoveHandlerMapping(key, database);
        }

        foreach (var key in newAssociations)
        {
            if (!originalKeys.Contains(key))
                handlerMappingService.SetHandlerMapping(key, appId, database);
        }
    }

    /// <summary>
    /// Syncs registry handler registrations with the current effective mappings.
    /// Must be called after the database has been saved successfully.
    /// </summary>
    public void SyncRegistry()
    {
        var database = databaseProvider.GetDatabase();
        var updated = handlerMappingService.GetEffectiveHandlerMappings(database);
        handlerRegistrationService.Sync(updated, database.Apps);
    }
}