using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.Persistence;

/// <summary>
/// Encapsulates all save operations for app config files (main and additional).
/// Extracted from <see cref="AppConfigService"/> to keep that class focused on
/// load/unload/mapping state management.
/// </summary>
public class AppConfigSaveHelper(
    IGrantConfigTracker grantTracker,
    IHandlerMappingService handlerMappings,
    IDatabaseService databaseService)
{
    public void SaveConfigForApp(string? configPath, List<AppEntry> appsForConfig,
        AppDatabase database, byte[] pinDerivedKey, byte[] argonSalt)
    {
        if (configPath != null)
        {
            databaseService.SaveAppConfig(
                new AppConfig
                {
                    Apps = appsForConfig,
                    Accounts = grantTracker.FilterGrantsForConfig(database.Accounts, configPath),
                    SharedContainerTraverseGrants = grantTracker.FilterGrantsForConfig(
                        WellKnownSecuritySids.AllApplicationPackagesSid,
                        database.SharedContainerTraverseGrants,
                        configPath),
                    HandlerMappings = handlerMappings.GetHandlerMappingsForConfig(configPath)
                },
                configPath, pinDerivedKey, argonSalt);
        }
        else
        {
            databaseService.SaveConfig(database, pinDerivedKey, argonSalt);
        }
    }

    public void SaveConfigAtPath(string normalizedPath, List<AppEntry> apps,
        AppDatabase database, byte[] pinDerivedKey, byte[] argonSalt)
    {
        databaseService.SaveAppConfig(
            new AppConfig
            {
                Apps = apps,
                Accounts = grantTracker.FilterGrantsForConfig(database.Accounts, normalizedPath),
                SharedContainerTraverseGrants = grantTracker.FilterGrantsForConfig(
                    WellKnownSecuritySids.AllApplicationPackagesSid,
                    database.SharedContainerTraverseGrants,
                    normalizedPath),
                HandlerMappings = handlerMappings.GetHandlerMappingsForConfig(normalizedPath)
            },
            normalizedPath, pinDerivedKey, argonSalt);
    }

    public void SaveAllConfigs(IReadOnlyList<(string Path, List<AppEntry> Apps)> additionalConfigs,
        AppDatabase database, byte[] pinDerivedKey, byte[] argonSalt)
    {
        databaseService.SaveConfig(database, pinDerivedKey, argonSalt);

        foreach (var (path, apps) in additionalConfigs)
        {
            databaseService.SaveAppConfig(
                new AppConfig
                {
                    Apps = apps,
                    Accounts = grantTracker.FilterGrantsForConfig(database.Accounts, path),
                    SharedContainerTraverseGrants = grantTracker.FilterGrantsForConfig(
                        WellKnownSecuritySids.AllApplicationPackagesSid,
                        database.SharedContainerTraverseGrants,
                        path),
                    HandlerMappings = handlerMappings.GetHandlerMappingsForConfig(path)
                },
                path, pinDerivedKey, argonSalt);
        }
    }

    public void ReencryptAndSaveAll(CredentialStore store,
        IReadOnlyList<(string Path, List<AppEntry> Apps)> additionalConfigs,
        AppDatabase database, byte[] newPinDerivedKey)
    {
        var configs = additionalConfigs.Select(c => (c.Path,
            new AppConfig
            {
                Apps = c.Apps,
                Accounts = grantTracker.FilterGrantsForConfig(database.Accounts, c.Path),
                SharedContainerTraverseGrants = grantTracker.FilterGrantsForConfig(
                    WellKnownSecuritySids.AllApplicationPackagesSid,
                    database.SharedContainerTraverseGrants,
                    c.Path),
                HandlerMappings = handlerMappings.GetHandlerMappingsForConfig(c.Path)
            })).ToList();

        databaseService.SaveCredentialStoreAndAllConfigs(store, database, newPinDerivedKey, configs);
    }

    public void SaveImportedConfig(string path, AppConfig config,
        byte[] pinDerivedKey, byte[] argonSalt)
    {
        var normalized = Path.GetFullPath(path);
        databaseService.SaveAppConfig(config, normalized, pinDerivedKey, argonSalt);
    }
}
