using RunFence.Account;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;

namespace RunFence.Persistence.UI;

public class MainConfigImportController(
    ISessionProvider sessionProvider,
    IAccountSidResolutionService sidResolutionService,
    ConfigImportHandler importHandler,
    IHandlerMappingService handlerMappingService,
    IHandlerSyncService handlerSyncService,
    ILoggingService log)
{
    public async Task<MainConfigImportPresentationResult> ImportAsync(
        string importJsonPath,
        Action publishDataChanged,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var session = sessionProvider.GetSession();
        var previousKeys = GetEffectiveAssociationKeys(session.Database);
        var sidResolutions = await sidResolutionService.ResolveSidsAsync(
            session.CredentialStore,
            session.Database.SidNames);
        cancellationToken.ThrowIfCancellationRequested();

        var result = importHandler.ImportMainConfig(importJsonPath, sidResolutions);
        var currentKeys = GetEffectiveAssociationKeys(session.Database);
        var removedKeys = previousKeys
            .Except(currentKeys, StringComparer.OrdinalIgnoreCase)
            .ToList();

        publishDataChanged();

        var warnings = result.Warnings.ToList();
        try
        {
            if (removedKeys.Count > 0)
                handlerSyncService.Sync(removedKeys);
            else
                handlerSyncService.Sync();
        }
        catch (Exception ex)
        {
            log.Error("Main config import handler sync failed", ex);
            warnings.Add($"Handler sync failed: {ex.Message}");
        }

        return new MainConfigImportPresentationResult(
            true,
            warnings,
            result.SaveError);
    }

    private HashSet<string> GetEffectiveAssociationKeys(AppDatabase database)
    {
        var keys = handlerMappingService.GetEffectiveHandlerMappings(database).Keys
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        keys.UnionWith(handlerMappingService.GetEffectiveDirectHandlerMappings(database).Keys);
        return keys;
    }
}

public sealed record MainConfigImportPresentationResult(
    bool Succeeded,
    IReadOnlyList<string> Warnings,
    string? SaveError);
