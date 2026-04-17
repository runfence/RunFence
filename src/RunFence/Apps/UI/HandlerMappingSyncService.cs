using RunFence.Apps;
using RunFence.Core.Models;
using RunFence.Persistence;

namespace RunFence.Apps.UI;

/// <summary>
/// Syncs HKLM handler registrations and HKCU auto-set overrides to match the current database state.
/// </summary>
public class HandlerMappingSyncService(
    IHandlerMappingService handlerMappingService,
    IAppHandlerRegistrationService handlerRegistrationService,
    IAssociationAutoSetService autoSetService)
{
    /// <summary>
    /// Syncs HKLM handler registrations and HKCU auto-set overrides to match the database state.
    /// </summary>
    public void Sync(AppDatabase database)
    {
        var effective = handlerMappingService.GetEffectiveHandlerMappings(database);
        handlerRegistrationService.Sync(effective, database.Apps);
        autoSetService.AutoSetForAllUsers();
    }
}
