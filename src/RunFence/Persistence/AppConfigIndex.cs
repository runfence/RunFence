using RunFence.Core.Models;

namespace RunFence.Persistence;

/// <summary>
/// Authoritative owner of the additional-config app mapping and loaded-path state.
/// Implements <see cref="IAppFilter"/> so <see cref="DatabaseService"/> can filter
/// main-config saves without depending on <see cref="AppConfigService"/>.
/// </summary>
public class AppConfigIndex(IGrantConfigTracker grantTracker) : IAppFilter
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
            .ToList();

        return new AppDatabase
        {
            Apps = mainApps,
            Settings = database.Settings,
            LastPrefsFilePath = database.LastPrefsFilePath,
            SidNames = database.SidNames,
            AppContainers = database.AppContainers,
            Accounts = FilterAccountsForMainConfig(database.Accounts),
            AccountGroupSnapshots = database.AccountGroupSnapshots,
        };
    }

    private List<AccountEntry> FilterAccountsForMainConfig(List<AccountEntry> accounts)
    {
        var result = new List<AccountEntry>();
        foreach (var account in accounts)
        {
            var mainGrants = account.Grants
                .Where(e => grantTracker.IsInMainConfig(account.Sid, e))
                .ToList();
            var clone = account.Clone();
            clone.Grants = mainGrants;
            if (!clone.IsEmpty)
                result.Add(clone);
        }

        return result;
    }

    // --- Query ---

    public string? GetConfigPath(string appId) => _appConfigMap.GetValueOrDefault(appId);

    public bool ContainsApp(string appId) => _appConfigMap.ContainsKey(appId);

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

    // --- Mutation ---

    public void AssignApp(string appId, string normalizedPath) => _appConfigMap[appId] = normalizedPath;

    public void UnassignApp(string appId) => _appConfigMap.Remove(appId);

    public void AddLoadedPath(string normalizedPath) => _loadedPaths.Add(normalizedPath);

    public void RemoveLoadedPath(string normalizedPath) =>
        _loadedPaths.RemoveAll(p => string.Equals(p, normalizedPath, StringComparison.OrdinalIgnoreCase));
}
