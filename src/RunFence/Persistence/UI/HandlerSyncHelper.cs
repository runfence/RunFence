using RunFence.Apps;
using RunFence.Infrastructure;

namespace RunFence.Persistence.UI;

/// <summary>
/// Syncs app handler registrations after config load or unload.
/// </summary>
public class HandlerSyncHelper(
    ISessionProvider sessionProvider,
    IAppHandlerRegistrationService handlerRegistrationService,
    IHandlerMappingService handlerMappingService,
    IAssociationAutoSetService autoSetService)
{
    public void Sync() => Sync(removedAssociationKeys: null);

    public void Sync(IReadOnlyCollection<string>? removedAssociationKeys)
    {
        var database = sessionProvider.GetSession().Database;
        var newMappings = handlerMappingService.GetEffectiveHandlerMappings(database);

        if (removedAssociationKeys != null)
        {
            foreach (var key in removedAssociationKeys)
                autoSetService.RestoreKeyForAllUsers(key);
        }

        handlerRegistrationService.Sync(newMappings, database.Apps);
        autoSetService.AutoSetForAllUsers();
    }
}