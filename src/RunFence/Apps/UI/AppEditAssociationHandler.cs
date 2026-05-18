using RunFence.Persistence;

namespace RunFence.Apps.UI;

/// <summary>
/// Manages handler association state for <see cref="Forms.AppEditDialog"/>:
/// populates the associations section from the database, applies in-memory mapping changes,
/// and syncs registry registrations after a successful save.
/// </summary>
public class AppEditAssociationHandler(
    IHandlerMappingService handlerMappingService,
    IAppHandlerRegistrationService handlerRegistrationService,
    IAssociationAutoSetService autoSetService,
    IDatabaseProvider databaseProvider,
    Func<HandlerMappingMutationHandler> mutationHandlerFactory)
{
    private IReadOnlyList<string> _pendingRemovedAssociations = [];

    private readonly HandlerMappingMutationHandler _mutationHandler = mutationHandlerFactory();

    /// <summary>
    /// Returns the association items currently mapped to <paramref name="appId"/> across all loaded configs.
    /// </summary>
    public IReadOnlyList<HandlerAssociationItem>? GetCurrentAssociations(string appId)
    {
        var database = databaseProvider.GetDatabase();
        var allMappings = handlerMappingService.GetAllHandlerMappings(database);
        var items = allMappings
            .Where(kv => kv.Value.Any(e => string.Equals(e.AppId, appId, StringComparison.OrdinalIgnoreCase)))
            .Select(kv =>
            {
                var e = kv.Value.First(e2 => string.Equals(e2.AppId, appId, StringComparison.OrdinalIgnoreCase));
                return new HandlerAssociationItem(kv.Key, e.ArgumentsTemplate, e.PathPrefixes?.AsReadOnly(), e.ReplacePrefixes);
            })
            .ToList();

        return items.Count > 0 ? items.AsReadOnly() : null;
    }

    /// <summary>
    /// Applies in-memory handler mapping changes (adds/removes/template/prefix updates) to the database
    /// based on the diff between current and new associations.
    /// Stores removed keys internally for use by <see cref="SyncRegistry"/>.
    /// Must be called before saving so the correct config file is written.
    /// </summary>
    public void ApplyChanges(string appId, IReadOnlyList<HandlerAssociationItem> newAssociations)
    {
        _pendingRemovedAssociations = _mutationHandler.SetAssociationsForApp(
            databaseProvider.GetDatabase(),
            appId,
            newAssociations);
    }

    /// <summary>
    /// Reverts in-memory handler mapping changes made by <see cref="ApplyChanges"/> by restoring
    /// the original associations, and clears the pending removed-key state.
    /// Called on save failure to prevent a diverged in-memory state.
    /// </summary>
    public void RevertChanges(string appId, IReadOnlyList<HandlerAssociationItem> originalAssociations)
    {
        _mutationHandler.SetAssociationsForApp(
            databaseProvider.GetDatabase(),
            appId,
            originalAssociations);
        _pendingRemovedAssociations = [];
    }

    /// <summary>
    /// Syncs registry handler registrations with the current effective mappings.
    /// Uses removed keys stored by the preceding <see cref="ApplyChanges"/> call to restore
    /// HKCU entries for any keys no longer mapped in any config.
    /// Must be called after the database has been saved successfully.
    /// </summary>
    public void SyncRegistry()
    {
        var database = databaseProvider.GetDatabase();
        // Restore HKCU only for keys that are no longer mapped in any config
        var remaining = handlerMappingService.GetAllHandlerMappings(database);
        foreach (var key in _pendingRemovedAssociations)
        {
            if (!remaining.ContainsKey(key))
                autoSetService.RestoreKeyForAllUsers(key);
        }

        var updated = handlerMappingService.GetEffectiveHandlerMappings(database);
        handlerRegistrationService.Sync(updated, database.Apps);
        autoSetService.AutoSetForAllUsers();
        _pendingRemovedAssociations = [];
    }

    /// <summary>
    /// Applies DB mapping changes and syncs HKLM/HKCU registry registrations in one operation.
    /// Equivalent to calling <see cref="ApplyChanges"/> followed immediately by <see cref="SyncRegistry"/>.
    /// Use when there is no intervening save step between apply and sync.
    /// </summary>
    public void ApplyAndSync(string appId, IReadOnlyList<HandlerAssociationItem> newAssociations)
    {
        ApplyChanges(appId, newAssociations);
        SyncRegistry();
    }
}
