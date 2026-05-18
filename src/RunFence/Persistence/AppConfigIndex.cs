using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.Persistence;

/// <summary>
/// Authoritative owner of the additional-config app mapping and loaded-path state.
/// Implements <see cref="IAppFilter"/> so <see cref="DatabaseService"/> can filter
/// main-config saves without depending on <see cref="AppConfigService"/>.
/// </summary>
public class AppConfigIndex(
    GrantIntentOwnershipProjectionService ownershipProjection,
    AppIdValidator appIdValidator)
    : IAppFilter
{
    // appId → normalized config path (absent = main config)
    private readonly Dictionary<string, string> _appConfigMap =
        new(StringComparer.OrdinalIgnoreCase);

    // loaded (normalized) config paths, in insertion order
    private readonly List<string> _loadedPaths = [];

    // --- IAppFilter ---

    /// <summary>
    /// Returns a shallow clone of the database containing only main-config apps and grants.
    /// Pure data transform — no I/O.
    /// </summary>
    // WARNING: When adding new AppDatabase properties, update this method.
    public AppDatabase FilterForMainConfig(AppDatabase database)
    {
        var mainApps = database.Apps
            .Where(a => !_appConfigMap.ContainsKey(a.Id))
            .Select(a => a.Clone())
            .ToList();
        var accounts = new List<AccountEntry>();
        foreach (var account in database.Accounts)
        {
            var clone = account.Clone();
            if (ownershipProjection.HasRegisteredAdditionalConfigs)
            {
                clone.Grants = account.Grants
                    .Where(entry => ownershipProjection.HasMainOwnership(account.Sid, entry))
                    .Select(entry => entry.Clone())
                    .ToList();
            }

            if (!clone.IsEmpty)
                accounts.Add(clone);
        }

        return new AppDatabase
        {
            Apps = mainApps,
            Settings = database.Settings.Clone(),
            LastPrefsFilePath = database.LastPrefsFilePath,
            SidNames = new Dictionary<string, string>(database.SidNames, StringComparer.OrdinalIgnoreCase),
            JobKeeperInstances = database.JobKeeperInstances?.ToDictionary(
                kvp => kvp.Key, kvp => kvp.Value with { }, StringComparer.OrdinalIgnoreCase),
            AppContainers = database.AppContainers.Select(c => c.Clone()).ToList(),
            Accounts = accounts,
            AccountGroupSnapshots = database.AccountGroupSnapshots?.ToDictionary(
                kvp => kvp.Key, kvp => kvp.Value.ToList(), StringComparer.OrdinalIgnoreCase),
            ShowSystemInRunAs = database.ShowSystemInRunAs,
        };
    }

    // --- Query ---

    public string? GetConfigPath(string appId)
    {
        appIdValidator.EnsureValidAppId(appId, "App ID");
        return _appConfigMap.GetValueOrDefault(appId);
    }

    public bool ContainsLoadedPath(string normalizedPath) =>
        _loadedPaths.Any(p => string.Equals(p, normalizedPath, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<string> GetLoadedConfigPaths() => _loadedPaths;

    public bool HasLoadedConfigs => _loadedPaths.Count > 0;

    public List<string> GetUnavailableConfigPaths() =>
        _loadedPaths.Where(p => !File.Exists(p)).ToList();

    public List<AppEntry> GetAppsForConfig(string normalizedPath, AppDatabase database) =>
        database.Apps
            .Where(a => _appConfigMap.TryGetValue(a.Id, out var p) &&
                        string.Equals(p, normalizedPath, StringComparison.OrdinalIgnoreCase))
            .ToList();

    internal AppConfigIndexStateSnapshot CaptureStateSnapshot()
        => new(
            new Dictionary<string, string>(_appConfigMap, StringComparer.OrdinalIgnoreCase),
            _loadedPaths.ToList());

    // --- Mutation ---

    public void AssignApp(string appId, string normalizedPath)
    {
        appIdValidator.EnsureValidAppId(appId, "App ID");
        _appConfigMap[appId] = normalizedPath;
    }

    public void UnassignApp(string appId)
    {
        appIdValidator.EnsureValidAppId(appId, "App ID");
        _appConfigMap.Remove(appId);
    }

    public void AddLoadedPath(string normalizedPath) => _loadedPaths.Add(normalizedPath);

    public void RemoveLoadedPath(string normalizedPath) =>
        _loadedPaths.RemoveAll(p => string.Equals(p, normalizedPath, StringComparison.OrdinalIgnoreCase));

    internal void RestoreStateSnapshot(AppConfigIndexStateSnapshot snapshot)
    {
        _appConfigMap.Clear();
        foreach (var (appId, configPath) in snapshot.AppConfigMap)
            _appConfigMap[appId] = configPath;

        _loadedPaths.Clear();
        _loadedPaths.AddRange(snapshot.LoadedPaths);
    }
}
