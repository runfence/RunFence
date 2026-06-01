using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.Persistence;

public class AppConfigSaveHelper(
    Func<IGrantIntentStoreProvider> grantIntentStoreProvider,
    IHandlerMappingService handlerMappings,
    IDatabaseService databaseService)
{
    public void SaveConfigForApp(string? configPath, List<AppEntry> appsForConfig,
        AppDatabase database, ISecureSecretSnapshotSource pinDerivedKey, byte[] argonSalt)
        => SaveConfigForAppCore(
            configPath,
            appsForConfig,
            database,
            saveMainConfig: () => databaseService.SaveConfig(database, pinDerivedKey, argonSalt),
            saveAdditionalConfig: (path, config) => databaseService.SaveAppConfig(config, path, pinDerivedKey, argonSalt));

    public void SaveConfigAtPath(string normalizedPath, List<AppEntry> apps,
        AppDatabase database, ISecureSecretSnapshotSource pinDerivedKey, byte[] argonSalt)
        => SaveAdditionalConfig(
            normalizedPath,
            apps,
            database,
            config => databaseService.SaveAppConfig(config, normalizedPath, pinDerivedKey, argonSalt));

    public void SaveAllConfigs(IReadOnlyList<(string Path, List<AppEntry> Apps)> additionalConfigs,
        AppDatabase database, ISecureSecretSnapshotSource pinDerivedKey, byte[] argonSalt)
        => SaveAllConfigsCore(
            additionalConfigs,
            database,
            saveMainConfig: () => databaseService.SaveConfig(database, pinDerivedKey, argonSalt),
            saveAdditionalConfig: (path, config) => databaseService.SaveAppConfig(config, path, pinDerivedKey, argonSalt));

    public void ReencryptAndSaveAll(CredentialStore store,
        IReadOnlyList<(string Path, List<AppEntry> Apps)> additionalConfigs,
        AppDatabase database, ISecureSecretSnapshotSource newPinDerivedKey)
    {
        var configs = BuildSavedConfigs(additionalConfigs, database);

        databaseService.SaveCredentialStoreAndAllConfigs(store, database, newPinDerivedKey, configs);
    }

    public void SaveImportedConfig(string path, AppConfig config,
        ISecureSecretSnapshotSource pinDerivedKey, byte[] argonSalt)
    {
        var normalized = AppConfigPathHelper.NormalizePath(path);
        databaseService.SaveAppConfig(config, normalized, pinDerivedKey, argonSalt);
    }

    private void SaveConfigForAppCore(
        string? configPath,
        List<AppEntry> appsForConfig,
        AppDatabase database,
        Action saveMainConfig,
        Action<string, AppConfig> saveAdditionalConfig)
    {
        if (configPath == null)
        {
            saveMainConfig();
            return;
        }

        SaveAdditionalConfig(
            configPath,
            appsForConfig,
            database,
            config => saveAdditionalConfig(configPath, config));
    }

    private void SaveAllConfigsCore(
        IReadOnlyList<(string Path, List<AppEntry> Apps)> additionalConfigs,
        AppDatabase database,
        Action saveMainConfig,
        Action<string, AppConfig> saveAdditionalConfig)
    {
        saveMainConfig();

        foreach (var (path, apps) in additionalConfigs)
        {
            SaveAdditionalConfig(
                path,
                apps,
                database,
                config => saveAdditionalConfig(path, config));
        }
    }

    private void SaveAdditionalConfig(
        string configPath,
        List<AppEntry> apps,
        AppDatabase database,
        Action<AppConfig> saveConfig)
        => saveConfig(BuildSavedConfig(configPath, apps, database));

    private List<(string path, AppConfig config)> BuildSavedConfigs(
        IReadOnlyList<(string Path, List<AppEntry> Apps)> additionalConfigs,
        AppDatabase database)
        => additionalConfigs
            .Select(config => (config.Path, BuildSavedConfig(config.Path, config.Apps, database)))
            .ToList();

    private AppConfig BuildSavedConfig(string configPath, List<AppEntry> apps, AppDatabase database)
    {
        var store = grantIntentStoreProvider().ResolveStore(configPath);
        return new AppConfig
        {
            Apps = apps.Select(app => app.Clone()).ToList(),
            Accounts = GrantIntentStoreConfigDataBuilder.BuildAccounts(store, database),
            HandlerMappings = handlerMappings.GetHandlerMappingsForConfig(configPath)
        };
    }
}
