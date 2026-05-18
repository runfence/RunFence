using RunFence.Persistence;

namespace RunFence.Apps.UI;

/// <summary>
/// Syncs HKLM handler registrations and HKCU auto-set overrides to match the current database state.
/// Callers are responsible for deciding when to save before or after sync and for surfacing warnings.
/// </summary>
public class HandlerMappingSyncService(
    IHandlerMappingService handlerMappingService,
    IAppHandlerRegistrationService handlerRegistrationService,
    IAssociationAutoSetService autoSetService,
    IDatabaseProvider databaseProvider)
{
    public HandlerMappingSyncResult Sync(IReadOnlyList<string>? keysToRestore = null)
    {
        try
        {
            var database = databaseProvider.GetDatabase();
            if (keysToRestore != null)
            {
                foreach (var key in keysToRestore.Distinct(StringComparer.OrdinalIgnoreCase))
                    autoSetService.RestoreKeyForAllUsers(key);
            }

            var effective = handlerMappingService.GetEffectiveHandlerMappings(database);
            handlerRegistrationService.Sync(effective, database.Apps);
            autoSetService.AutoSetForAllUsers();
            return new HandlerMappingSyncResult(true, null);
        }
        catch (Exception ex)
        {
            return new HandlerMappingSyncResult(false, ex.Message);
        }
    }
}

public sealed record HandlerMappingSyncResult(bool Succeeded, string? WarningMessage);
