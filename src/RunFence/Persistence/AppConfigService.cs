using System.Security.Cryptography;
using RunFence.Apps;
using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.Persistence;

/// <summary>
/// Manages additional app config files loaded from removable/external media.
/// All mutations happen on the UI thread (same contract as SessionContext).
/// </summary>
public class AppConfigService(
    ILoggingService log,
    AppConfigIndex index,
    GrantIntentOwnershipProjectionService ownershipProjection,
    Func<IGrantIntentStoreProvider> grantIntentStoreProvider,
    IHandlerMappingService handlerMappings,
    IDatabaseService databaseService,
    AppConfigSaveHelper saveHelper,
    IAppEntryIdGenerator idGenerator,
    AppIdValidator appIdValidator)
    : IAppConfigService
{
    public string? GetConfigPath(string appId) => index.GetConfigPath(appId);

    public List<AppEntry> GetAppsForConfig(string path, AppDatabase database) =>
        index.GetAppsForConfig(Normalize(path), database);

    public AppConfig GetConfigForExport(string? path, AppDatabase database)
    {
        if (path == null)
        {
            var mainDb = index.FilterForMainConfig(database);
            return new AppConfig
            {
                Apps = mainDb.Apps,
                Accounts = mainDb.Accounts.Count == 0
                    ? null
                    : mainDb.Accounts.Select(account => new AppConfigAccountEntry
                    {
                        Sid = account.Sid,
                        Grants = account.Grants.Select(entry => entry.Clone()).ToList()
                    }).ToList(),
                HandlerMappings = database.Settings.HandlerMappings != null
                    ? new Dictionary<string, HandlerMappingEntry>(
                        database.Settings.HandlerMappings,
                        StringComparer.OrdinalIgnoreCase)
                    : null,
            };
        }

        var normalized = Normalize(path);
        var store = grantIntentStoreProvider().ResolveStore(normalized);
        return new AppConfig
        {
            Apps = GetAppsForConfig(normalized, database),
            Accounts = GrantIntentStoreConfigDataBuilder.BuildAccounts(store, database),
            HandlerMappings = handlerMappings.GetHandlerMappingsForConfig(normalized),
        };
    }

    public IReadOnlyList<string> GetLoadedConfigPaths() => index.GetLoadedConfigPaths();

    public bool HasLoadedConfigs => index.HasLoadedConfigs;

    public AppConfigRuntimeStateSnapshot CaptureRuntimeStateSnapshot()
    {
        var indexSnapshot = index.CaptureStateSnapshot();
        var handlerMappingsByConfigPath = indexSnapshot.LoadedPaths.ToDictionary(
            path => path,
            path => (IReadOnlyDictionary<string, HandlerMappingEntry>)CloneHandlerMappings(
                handlerMappings.GetHandlerMappingsForConfig(path)),
            StringComparer.OrdinalIgnoreCase);
        return new AppConfigRuntimeStateSnapshot(
            indexSnapshot.AppConfigMap,
            indexSnapshot.LoadedPaths,
            handlerMappingsByConfigPath,
            ownershipProjection.CaptureSnapshot());
    }

    public void RestoreRuntimeStateSnapshot(AppConfigRuntimeStateSnapshot snapshot)
    {
        var currentLoadedPaths = index.GetLoadedConfigPaths().ToList();
        foreach (var path in currentLoadedPaths)
            handlerMappings.UnregisterConfigMappings(path);

        index.RestoreStateSnapshot(new AppConfigIndexStateSnapshot(
            snapshot.AppConfigMap,
            snapshot.LoadedPaths));

        foreach (var configPath in snapshot.LoadedPaths)
        {
            snapshot.HandlerMappingsByConfigPath.TryGetValue(configPath, out var mappings);
            handlerMappings.RegisterConfigMappings(configPath, CloneHandlerMappings(mappings));
        }

        ownershipProjection.RestoreSnapshot(snapshot.OwnershipProjectionSnapshot);
    }

    public AdditionalConfigLoadData ReadAdditionalConfig(string path, AppDatabase database, ISecureSecretSnapshotSource pinDerivedKey)
        => ReadAdditionalConfigCore(path, database, normalizedPath => databaseService.LoadAppConfigFromPath(normalizedPath, pinDerivedKey));

    public AdditionalConfigLoadData ReadAdditionalConfigFromBackup(string configPath, AppConfig backupConfig, AppDatabase database)
        => ReadAdditionalConfigCore(configPath, database, _ => backupConfig);

    private AdditionalConfigLoadData ReadAdditionalConfigCore(
        string path,
        AppDatabase database,
        Func<string, AppConfig> loadConfig)
    {
        var normalized = Normalize(path);

        var roaming = NormalizeDir(PathConstants.RoamingAppDataDir);
        var local = NormalizeDir(PathConstants.LocalAppDataDir);
        if (normalized.StartsWith(roaming, StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith(local, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Cannot load config from app's own data directories.", nameof(path));
        }

        if (index.ContainsLoadedPath(normalized))
        {
            log.Warn($"Config path already loaded, ignoring duplicate load: {normalized}");
            return new AdditionalConfigLoadData(
                normalized,
                [],
                [],
                new Dictionary<string, HandlerMappingEntry>(StringComparer.OrdinalIgnoreCase),
                SkipCommit: true);
        }

        AppConfig config;
        try
        {
            config = loadConfig(normalized);
        }
        catch (CryptographicException ex)
        {
            throw new CryptographicException(
                "Cannot decrypt config file. It may be encrypted with a different PIN.", ex);
        }

        var currentSid = SidResolutionHelper.GetCurrentUserSid();
        var existingIds = database.Apps.Select(a => a.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var databaseIds = new HashSet<string>(existingIds, StringComparer.OrdinalIgnoreCase);
        var seenImportedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var renamedAppIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var loadedApps = new List<AppEntry>();
        foreach (var app in config.Apps)
        {
            appIdValidator.EnsureValidAppId(app.Id, $"Imported app ID for '{app.Name}'");

            if (string.IsNullOrEmpty(app.AccountSid) && app.AppContainerName == null)
                app.AccountSid = currentSid;

            var originalId = app.Id;
            var firstImportedOccurrence = seenImportedIds.Add(originalId);
            if (existingIds.Contains(originalId))
            {
                var newId = idGenerator.GenerateUniqueId(existingIds);
                log.Info($"App '{app.Name}' had ID collision, regenerated: {app.Id} -> {newId}");
                app.Id = newId;
                if (firstImportedOccurrence && databaseIds.Contains(originalId))
                    renamedAppIds[originalId] = newId;
            }

            existingIds.Add(app.Id);
            loadedApps.Add(app);
        }

        var accounts = config.Accounts ?? [];
        return new AdditionalConfigLoadData(
            normalized,
            loadedApps,
            accounts,
            handlerMappings.StageConfigMappings(
                config.HandlerMappings,
                renamedAppIds,
                loadedApps.Select(app => app.Id).ToHashSet(StringComparer.OrdinalIgnoreCase)));
    }

    public List<AppEntry> ApplyAdditionalConfig(AdditionalConfigLoadData configData, AppDatabase database)
    {
        if (configData.SkipCommit)
            return [];

        if (!index.HasLoadedConfigs)
            ownershipProjection.CaptureMainOwnershipBaseline(database);

        grantIntentStoreProvider().RegisterAdditionalStore(
            configData.NormalizedPath,
            configData.Accounts);
        index.AddLoadedPath(configData.NormalizedPath);

        foreach (var app in configData.Apps)
        {
            database.Apps.Add(app);
            index.AssignApp(app.Id, configData.NormalizedPath);
        }

        foreach (var configAccount in configData.Accounts.ToList())
        {
            var dbAccount = database.GetOrCreateAccount(configAccount.Sid);
            foreach (var entry in (configAccount.Grants ?? []).ToList())
            {
                var existingEntry = FindEquivalentEntry(dbAccount.Grants, entry);
                if (existingEntry != null)
                    continue;

                dbAccount.Grants.Add(entry);
            }
        }

        handlerMappings.RegisterConfigMappings(configData.NormalizedPath, configData.HandlerMappings);

        log.Info($"Loaded {configData.Apps.Count} app(s) from {configData.NormalizedPath}");
        return configData.Apps;
    }

    public List<AppEntry> LoadAdditionalConfig(string path, AppDatabase database, ISecureSecretSnapshotSource pinDerivedKey)
    {
        var configData = ReadAdditionalConfig(path, database, pinDerivedKey);
        return ApplyAdditionalConfig(configData, database);
    }

    public List<AppEntry> UnloadConfig(string path, AppDatabase database)
    {
        var normalized = Normalize(path);
        if (!index.ContainsLoadedPath(normalized))
        {
            log.Info($"Config path not loaded, nothing to unload: {normalized}");
            return [];
        }

        var configForRemoval = GetConfigForExport(normalized, database);
        var removableAccountEntries = GetEntriesWithoutRemainingOwnership(
            configForRemoval.Accounts,
            normalized);
        grantIntentStoreProvider().UnregisterAdditionalStore(normalized);

        var removedApps = index.GetAppsForConfig(normalized, database);
        foreach (var app in removedApps)
        {
            database.Apps.Remove(app);
            index.UnassignApp(app.Id);
        }

        handlerMappings.UnregisterConfigMappings(normalized);
        index.RemoveLoadedPath(normalized);
        RemoveAccountGrants(removableAccountEntries, database);
        log.Info($"Unloaded {removedApps.Count} app(s) from {normalized}");
        return removedApps;
    }

    public void CreateEmptyConfig(string path, ISecureSecretSnapshotSource pinDerivedKey, byte[] argonSalt)
    {
        var normalized = Normalize(path);
        databaseService.SaveAppConfig(new AppConfig(), normalized, pinDerivedKey, argonSalt);
        log.Info($"Created empty config at {normalized}");
    }

    public void AssignApp(string appId, string? configPath)
    {
        appIdValidator.EnsureValidAppId(appId, "App ID");
        var normalized = configPath != null ? Normalize(configPath) : null;
        if (normalized == null)
            index.UnassignApp(appId);
        else
            index.AssignApp(appId, normalized);
    }

    public void RemoveApp(string appId)
    {
        index.UnassignApp(appId);
    }

    public void SaveConfigForApp(string appId, AppDatabase database, ISecureSecretSnapshotSource pinDerivedKey, byte[] argonSalt)
    {
        var configPath = GetConfigPath(appId);
        var apps = configPath != null ? GetAppsForConfig(configPath, database) : [];
        saveHelper.SaveConfigForApp(configPath, apps, database, pinDerivedKey, argonSalt);
    }

    public void SaveConfigAtPath(string configPath, AppDatabase database, ISecureSecretSnapshotSource pinDerivedKey, byte[] argonSalt)
    {
        var normalized = Path.GetFullPath(configPath);
        var apps = GetAppsForConfig(normalized, database);
        saveHelper.SaveConfigAtPath(normalized, apps, database, pinDerivedKey, argonSalt);
    }

    public void SaveAllConfigs(AppDatabase database, ISecureSecretSnapshotSource pinDerivedKey, byte[] argonSalt)
    {
        var additionalConfigs = GetLoadedConfigPaths()
            .Select(path => (path, GetAppsForConfig(path, database)))
            .ToList();
        saveHelper.SaveAllConfigs(additionalConfigs, database, pinDerivedKey, argonSalt);
    }

    public void ReencryptAndSaveAll(CredentialStore store, AppDatabase database, ISecureSecretSnapshotSource newPinDerivedKey)
    {
        var additionalConfigs = GetLoadedConfigPaths()
            .Select(path => (path, GetAppsForConfig(path, database)))
            .ToList();
        saveHelper.ReencryptAndSaveAll(store, additionalConfigs, database, newPinDerivedKey);
    }

    public void SaveImportedConfig(string path, AppConfig config, ISecureSecretSnapshotSource pinDerivedKey, byte[] argonSalt)
    {
        ValidateAppIds(config.Apps);
        saveHelper.SaveImportedConfig(path, config, pinDerivedKey, argonSalt);
    }

    private static Dictionary<string, HandlerMappingEntry> CloneHandlerMappings(
        IReadOnlyDictionary<string, HandlerMappingEntry>? mappings)
        => mappings?.ToDictionary(
               kvp => kvp.Key,
               kvp => new HandlerMappingEntry(
                   kvp.Value.AppId,
                   kvp.Value.ArgumentsTemplate,
                   kvp.Value.PathPrefixes?.ToList(),
                   kvp.Value.ReplacePrefixes),
               StringComparer.OrdinalIgnoreCase)
           ?? new Dictionary<string, HandlerMappingEntry>(StringComparer.OrdinalIgnoreCase);

    private static string Normalize(string path) => Path.GetFullPath(path);

    private static string NormalizeDir(string dir)
    {
        var p = Path.GetFullPath(dir);
        return p.EndsWith(Path.DirectorySeparatorChar) ? p : p + Path.DirectorySeparatorChar;
    }

    private void ValidateAppIds(IEnumerable<AppEntry> apps)
    {
        foreach (var app in apps)
            appIdValidator.EnsureValidAppId(app.Id, $"App ID for '{app.Name}'");
    }

    private static void RemoveAccountGrants(
        List<AppConfigAccountEntry>? accounts,
        AppDatabase database)
    {
        if (accounts == null)
            return;

        foreach (var account in accounts)
        {
            var dbAccount = database.GetAccount(account.Sid);
            if (dbAccount == null)
                continue;

            foreach (var grant in account.Grants)
            {
                var identity = GrantIntentEntryIdentity.From(account.Sid, grant);
                dbAccount.Grants.RemoveAll(existing =>
                    GrantIntentEntryIdentity.From(account.Sid, existing) == identity);
            }

            database.RemoveAccountIfEmpty(account.Sid);
        }
    }

    private static GrantedPathEntry? FindEquivalentEntry(
        IEnumerable<GrantedPathEntry> entries,
        GrantedPathEntry candidate)
        => entries.FirstOrDefault(entry =>
            GrantIntentEntryIdentity.From("", entry) == GrantIntentEntryIdentity.From("", candidate));

    private List<AppConfigAccountEntry>? GetEntriesWithoutRemainingOwnership(
        List<AppConfigAccountEntry>? accounts,
        string unloadingConfigPath)
    {
        if (accounts == null)
            return null;

        var result = new List<AppConfigAccountEntry>();
        foreach (var account in accounts)
        {
            var removableEntries = account.Grants
                .Where(entry => !HasRemainingGrantLocationOutsideConfig(account.Sid, entry, unloadingConfigPath))
                .Select(entry => entry.Clone())
                .ToList();
            if (removableEntries.Count == 0)
                continue;

            result.Add(new AppConfigAccountEntry
            {
                Sid = account.Sid,
                Grants = removableEntries
            });
        }

        return result.Count == 0 ? null : result;
    }

    private bool HasRemainingGrantLocationOutsideConfig(
        string sid,
        GrantedPathEntry entry,
        string unloadingConfigPath)
        => ownershipProjection.HasOwnershipOutsideConfig(unloadingConfigPath, sid, entry);
}
