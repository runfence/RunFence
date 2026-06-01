using RunFence.Apps;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;

namespace RunFence.Persistence;

public class MainConfigImportRepairService(
    IAppConfigService appConfigService,
    IHandlerMappingService handlerMappingService,
    ILoggingService log,
    IAppEntryIdGenerator idGenerator,
    AppIdValidator appIdValidator)
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

    public MainConfigImportRepairPlan PrepareAdditionalAppIdRepairs(
        AppDatabase importedDb,
        List<AppEntry> additionalApps)
    {
        var importedIds = importedDb.Apps
            .Select(app => app.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var repairedAdditionalApps = additionalApps
            .Select(app => app.Clone())
            .ToList();
        var renames = new List<MainConfigAdditionalAppIdRename>();

        for (var i = 0; i < repairedAdditionalApps.Count; i++)
        {
            var app = repairedAdditionalApps[i];
            appIdValidator.EnsureValidAppId(app.Id, $"Loaded app ID for '{app.Name}'");

            if (!importedIds.Contains(app.Id))
                continue;

            var oldAppId = app.Id;
            var configPath = appConfigService.GetConfigPath(additionalApps[i].Id);
            var newAppId = idGenerator.GenerateUniqueId(importedIds.Concat(repairedAdditionalApps.Select(a => a.Id)));
            log.Info($"ImportMainConfig: additional app '{app.Name}' had ID collision, regenerated: {oldAppId} -> {newAppId}");

            app.Id = newAppId;
            importedIds.Add(newAppId);

            if (configPath != null)
                renames.Add(new MainConfigAdditionalAppIdRename(configPath, oldAppId, newAppId));
        }

        return new MainConfigImportRepairPlan(repairedAdditionalApps, renames, []);
    }

    public List<string> GetOrphanedGrantSids(
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

        return orphanedSids;
    }

    public void RepairImportedAppIdCollisions(AppDatabase importedDb)
    {
        var importedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var app in importedDb.Apps)
        {
            appIdValidator.EnsureValidAppId(app.Id, $"Imported app ID for '{app.Name}'");

            if (importedIds.Add(app.Id))
                continue;

            var newId = idGenerator.GenerateUniqueId(importedIds);
            log.Info($"ImportMainConfig: imported app '{app.Name}' had ID collision, regenerated: {app.Id} -> {newId}");
            app.Id = newId;
            importedIds.Add(newId);
        }
    }

    public void ApplyAdditionalAppIdRepairs(MainConfigImportRepairPlan repairPlan)
    {
        foreach (var rename in repairPlan.AdditionalAppIdRenames)
        {
            appConfigService.RemoveApp(rename.OldAppId);
            appConfigService.AssignApp(rename.NewAppId, rename.ConfigPath);
            handlerMappingService.RenameAppIdInConfigMappings(
                rename.ConfigPath,
                rename.OldAppId,
                rename.NewAppId);
        }
    }

}
