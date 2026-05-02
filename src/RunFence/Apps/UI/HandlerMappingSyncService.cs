using RunFence.Persistence;

namespace RunFence.Apps.UI;

/// <summary>
/// Syncs HKLM handler registrations and HKCU auto-set overrides to match the current database state.
/// Subscribes to <see cref="HandlerMappingMutationHandler.Changed"/> via <see cref="Initialize"/>
/// so that sync happens automatically after every mutation without callers needing to call Sync explicitly.
/// </summary>
public class HandlerMappingSyncService(
    IHandlerMappingService handlerMappingService,
    IAppHandlerRegistrationService handlerRegistrationService,
    IAssociationAutoSetService autoSetService,
    IDatabaseProvider databaseProvider)
{
    private HandlerMappingMutationHandler? _currentHandler;

    /// <summary>
    /// Subscribes to <paramref name="handler"/>'s <see cref="HandlerMappingMutationHandler.Changed"/>
    /// event and unsubscribes from any previous handler. Must be called once per dialog session.
    /// </summary>
    public void Initialize(HandlerMappingMutationHandler handler)
    {
        if (_currentHandler != null)
            _currentHandler.Changed -= OnChanged;

        _currentHandler = handler;
        _currentHandler.Changed += OnChanged;
    }

    /// <summary>
    /// Unsubscribes from the current handler's <see cref="HandlerMappingMutationHandler.Changed"/>
    /// event and clears the reference. Must be called when the dialog closes.
    /// </summary>
    public void Detach()
    {
        if (_currentHandler != null)
        {
            _currentHandler.Changed -= OnChanged;
            _currentHandler = null;
        }
    }

    private void OnChanged() => Sync();

    /// <summary>
    /// Syncs HKLM handler registrations and HKCU auto-set overrides to match the current database state.
    /// </summary>
    public void Sync()
    {
        var database = databaseProvider.GetDatabase();
        var effective = handlerMappingService.GetEffectiveHandlerMappings(database);
        handlerRegistrationService.Sync(effective, database.Apps);
        autoSetService.AutoSetForAllUsers();
    }
}
