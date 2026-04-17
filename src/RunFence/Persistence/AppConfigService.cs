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
    IGrantConfigTracker grantTracker,
    IHandlerMappingService handlerMappings,
    IDatabaseService databaseService,
    AppConfigSaveHelper saveHelper,
    IAppEntryIdGenerator idGenerator)
    : IAppConfigService
{
    // --- Query ---

    public string? GetConfigPath(string appId) => index.GetConfigPath(appId);

    public List<AppEntry> GetAppsForConfig(string path, AppDatabase database) =>
        index.GetAppsForConfig(Normalize(path), database);

    public IReadOnlyList<string> GetLoadedConfigPaths() => index.GetLoadedConfigPaths();

    public bool HasLoadedConfigs => index.HasLoadedConfigs;

    public List<string> GetUnavailableConfigPaths() => index.GetUnavailableConfigPaths();

    // --- Load / Unload ---

    public List<AppEntry> LoadAdditionalConfig(string path, AppDatabase database,
        byte[] pinDerivedKey)
    {
        var normalized = Normalize(path);

        // Validate: reject paths under app's own data directories
        var roaming = NormalizeDir(Constants.RoamingAppDataDir);
        var local = NormalizeDir(Constants.LocalAppDataDir);
        if (normalized.StartsWith(roaming, StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith(local, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Cannot load config from app's own data directories.", nameof(path));
        }

        // Guard against duplicate loads (List lacks HashSet's implicit deduplication)
        if (index.ContainsLoadedPath(normalized))
        {
            log.Warn($"Config path already loaded, ignoring duplicate load: {normalized}");
            return [];
        }

        AppConfig config;
        try
        {
            config = databaseService.LoadAppConfig(normalized, pinDerivedKey);
        }
        catch (CryptographicException ex)
        {
            throw new CryptographicException(
                "Cannot decrypt config file. It may be encrypted with a different PIN.", ex);
        }

        var currentSid = SidResolutionHelper.GetCurrentUserSid();
        var loadedApps = new List<AppEntry>();
        foreach (var app in config.Apps)
        {
            if (string.IsNullOrEmpty(app.AccountSid) && app.AppContainerName == null)
                app.AccountSid = currentSid;

            // ID collision: regenerate if already used
            if (database.Apps.Any(a => a.Id == app.Id))
            {
                var newId = idGenerator.GenerateUniqueId(database.Apps.Select(a => a.Id));
                log.Info($"App '{app.Name}' had ID collision, regenerated: {app.Id} → {newId}");
                app.Id = newId;
            }

            database.Apps.Add(app);
            index.AssignApp(app.Id, normalized);
            loadedApps.Add(app);
        }

        // Merge grants from this config into the database and register in the grant tracker
        if (config.Accounts != null)
        {
            foreach (var configAccount in config.Accounts)
            {
                var dbAccount = database.GetOrCreateAccount(configAccount.Sid);
                foreach (var entry in configAccount.Grants)
                {
                    if (dbAccount.Grants.Any(e =>
                            string.Equals(e.Path, entry.Path, StringComparison.OrdinalIgnoreCase) &&
                            e.IsDeny == entry.IsDeny &&
                            e.IsTraverseOnly == entry.IsTraverseOnly))
                        continue;

                    dbAccount.Grants.Add(entry);
                    grantTracker.AssignGrant(configAccount.Sid, entry, normalized);
                }
            }
        }

        // Register extra config with the handler mapping service (always, to maintain load order
        // for GetEffectiveHandlerMappings overlay, even if this config has no handler mappings now).
        handlerMappings.RegisterConfigMappings(normalized, config.HandlerMappings ?? new Dictionary<string, HandlerMappingEntry>());

        index.AddLoadedPath(normalized);
        log.Info($"Loaded {loadedApps.Count} app(s) from {normalized}");
        return loadedApps;
    }

    public List<AppEntry> UnloadConfig(string path, AppDatabase database)
    {
        var normalized = Normalize(path);
        if (!index.ContainsLoadedPath(normalized))
        {
            log.Info($"Config path not loaded, nothing to unload: {normalized}");
            return [];
        }

        var removedApps = index.GetAppsForConfig(normalized, database);
        foreach (var app in removedApps)
        {
            database.Apps.Remove(app);
            index.UnassignApp(app.Id);
        }

        // Remove grants tracked to this config
        var removedGrants = grantTracker.UnregisterConfigGrants(normalized);

        foreach (var (sid, grantPath, isDeny, isTraverseOnly) in removedGrants)
        {
            var account = database.GetAccount(sid);
            if (account != null)
            {
                account.Grants.RemoveAll(e =>
                    string.Equals(e.Path, grantPath, StringComparison.OrdinalIgnoreCase) &&
                    e.IsDeny == isDeny && e.IsTraverseOnly == isTraverseOnly);
                database.RemoveAccountIfEmpty(sid);
            }
        }

        handlerMappings.UnregisterConfigMappings(normalized);
        index.RemoveLoadedPath(normalized);
        log.Info($"Unloaded {removedApps.Count} app(s) from {normalized}");
        return removedApps;
    }

    public void CreateEmptyConfig(string path, byte[] pinDerivedKey, byte[] argonSalt)
    {
        var normalized = Normalize(path);
        databaseService.SaveAppConfig(new AppConfig(), normalized, pinDerivedKey, argonSalt);
        log.Info($"Created empty config at {normalized}");
    }

    // --- Mapping ---

    public void AssignApp(string appId, string? configPath)
    {
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

    // --- Save (delegated to AppConfigSaveHelper) ---

    public void SaveConfigForApp(string appId, AppDatabase database,
        byte[] pinDerivedKey, byte[] argonSalt)
    {
        var configPath = GetConfigPath(appId);
        var apps = configPath != null ? GetAppsForConfig(configPath, database) : [];
        saveHelper.SaveConfigForApp(configPath, apps, database, pinDerivedKey, argonSalt);
    }

    public void SaveConfigAtPath(string configPath, AppDatabase database,
        byte[] pinDerivedKey, byte[] argonSalt)
    {
        var normalized = Path.GetFullPath(configPath);
        var apps = GetAppsForConfig(normalized, database);
        saveHelper.SaveConfigAtPath(normalized, apps, database, pinDerivedKey, argonSalt);
    }

    public void SaveAllConfigs(AppDatabase database, byte[] pinDerivedKey, byte[] argonSalt)
    {
        var additionalConfigs = GetLoadedConfigPaths()
            .Select(path => (path, GetAppsForConfig(path, database)))
            .ToList();
        saveHelper.SaveAllConfigs(additionalConfigs, database, pinDerivedKey, argonSalt);
    }

    public void ReencryptAndSaveAll(CredentialStore store, AppDatabase database,
        byte[] newPinDerivedKey)
    {
        var additionalConfigs = GetLoadedConfigPaths()
            .Select(path => (path, GetAppsForConfig(path, database)))
            .ToList();
        saveHelper.ReencryptAndSaveAll(store, additionalConfigs, database, newPinDerivedKey);
    }

    public void SaveImportedConfig(string path, List<AppEntry> apps,
        byte[] pinDerivedKey, byte[] argonSalt)
        => saveHelper.SaveImportedConfig(path, apps, pinDerivedKey, argonSalt);

    // --- Private helpers ---

    private static string Normalize(string path) => Path.GetFullPath(path);

    private static string NormalizeDir(string dir)
    {
        var p = Path.GetFullPath(dir);
        return p.EndsWith(Path.DirectorySeparatorChar) ? p : p + Path.DirectorySeparatorChar;
    }
}
