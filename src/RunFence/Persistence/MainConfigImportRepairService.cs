using RunFence.Apps;
using RunFence.Acl;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;

namespace RunFence.Persistence;

public class MainConfigImportRepairService(
    IAppConfigService appConfigService,
    IHandlerMappingService handlerMappingService,
    IPathGrantService pathGrantService,
    ILoggingService log,
    IAppEntryIdGenerator idGenerator)
{
    public void ClearImportedAppTimestamps(AppDatabase importedDb)
    {
        foreach (var app in importedDb.Apps)
            app.LastKnownExeTimestamp = null;
    }

    public List<AppEntry> GetAdditionalApps(AppDatabase database) =>
        database.Apps
            .Where(app => appConfigService.GetConfigPath(app.Id) != null)
            .ToList();

    public void RemoveOrphanedGrantAces(
        AppDatabase database,
        AppDatabase importedDb,
        List<AppEntry> additionalApps)
    {
        var incomingSids = importedDb.Apps
            .Where(app => !string.IsNullOrEmpty(app.AccountSid))
            .Select(app => app.AccountSid)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var additionalSids = additionalApps
            .Where(app => !string.IsNullOrEmpty(app.AccountSid))
            .Select(app => app.AccountSid)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var orphanedSids = database.Apps
            .Where(app => appConfigService.GetConfigPath(app.Id) == null && !string.IsNullOrEmpty(app.AccountSid))
            .Select(app => app.AccountSid)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(sid => !incomingSids.Contains(sid) && !additionalSids.Contains(sid))
            .ToList();

        foreach (var sid in orphanedSids)
            pathGrantService.RemoveAll(sid, updateFileSystem: true);
    }

    public void RepairImportedAppIdCollisions(AppDatabase importedDb)
    {
        var importedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var app in importedDb.Apps)
        {
            if (importedIds.Add(app.Id))
                continue;

            var newId = idGenerator.GenerateUniqueId(importedIds);
            log.Info($"ImportMainConfig: imported app '{app.Name}' had ID collision, regenerated: {app.Id} -> {newId}");
            app.Id = newId;
            importedIds.Add(newId);
        }
    }

    public void RepairAdditionalAppIdCollisions(AppDatabase importedDb, List<AppEntry> additionalApps)
    {
        var importedIds = importedDb.Apps
            .Select(app => app.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var app in additionalApps)
        {
            if (!importedIds.Contains(app.Id))
                continue;

            var oldAppId = app.Id;
            var configPath = appConfigService.GetConfigPath(oldAppId);
            var newAppId = idGenerator.GenerateUniqueId(importedIds.Concat(additionalApps.Select(additionalApp => additionalApp.Id)));
            log.Info($"ImportMainConfig: additional app '{app.Name}' had ID collision, regenerated: {oldAppId} -> {newAppId}");

            app.Id = newAppId;
            importedIds.Add(newAppId);

            appConfigService.RemoveApp(oldAppId);
            appConfigService.AssignApp(newAppId, configPath);
            if (configPath != null)
                handlerMappingService.RenameAppIdInConfigMappings(configPath, oldAppId, newAppId);
        }
    }
}
