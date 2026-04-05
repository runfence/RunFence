using RunFence.Apps;
using RunFence.Infrastructure;

namespace RunFence.Persistence.UI;

/// <summary>
/// Syncs app handler registrations after config load or unload.
/// </summary>
public class HandlerSyncHelper(
    ISessionProvider sessionProvider,
    IAppHandlerRegistrationService handlerRegistrationService,
    IHandlerMappingService handlerMappingService)
{
    public void Sync()
    {
        var database = sessionProvider.GetSession().Database;
        var effectiveMappings = handlerMappingService.GetEffectiveHandlerMappings(database);
        handlerRegistrationService.Sync(effectiveMappings, database.Apps);
    }
}