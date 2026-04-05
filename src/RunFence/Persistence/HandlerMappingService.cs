using RunFence.Core.Models;

namespace RunFence.Persistence;

/// <summary>
/// Manages handler mappings (extension/protocol → app ID) across the main config and loaded
/// additional configs. Extra config mappings are held in memory; main config mappings live in
/// <see cref="AppDatabase"/>.
/// <para>
/// App-to-config assignments are kept in sync via <see cref="SetAppConfig"/>, which
/// <see cref="AppConfigService"/> calls on load, unload, and manual assignment. This allows
/// <see cref="SetHandlerMapping"/> to route mappings to the correct config file without a
/// dependency on <see cref="IAppConfigService"/>.
/// </para>
/// </summary>
public class HandlerMappingService : IHandlerMappingService
{
    // normalized config path → HandlerMappings for that extra config, in registration order
    private readonly Dictionary<string, Dictionary<string, string>> _extraHandlerMappings =
        new(StringComparer.OrdinalIgnoreCase);

    // tracks registration order for GetEffectiveHandlerMappings overlay
    private readonly List<string> _loadedPaths = [];

    // app ID → normalized config path (absent = main config)
    private readonly Dictionary<string, string> _appConfigMap =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Notifies this service that <paramref name="appId"/> has been assigned to
    /// <paramref name="configPath"/> (null = main config). Called by <see cref="AppConfigService"/>.
    /// </summary>
    public void SetAppConfig(string appId, string? configPath)
    {
        if (configPath == null)
            _appConfigMap.Remove(appId);
        else
            _appConfigMap[appId] = Path.GetFullPath(configPath);
    }

    public Dictionary<string, string> GetEffectiveHandlerMappings(AppDatabase database)
    {
        var result = database.Settings.HandlerMappings != null
            ? new Dictionary<string, string>(database.Settings.HandlerMappings, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in _loadedPaths)
        {
            if (_extraHandlerMappings.TryGetValue(path, out var extraMappings))
            {
                foreach (var kvp in extraMappings)
                    result[kvp.Key] = kvp.Value;
            }
        }

        return result;
    }

    public void SetHandlerMapping(string key, string appId, AppDatabase database)
    {
        RemoveHandlerMapping(key, database);

        _appConfigMap.TryGetValue(appId, out var targetConfigPath);

        if (targetConfigPath == null)
        {
            database.Settings.HandlerMappings ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            database.Settings.HandlerMappings[key] = appId;
        }
        else
        {
            if (!_extraHandlerMappings.TryGetValue(targetConfigPath, out var mappings))
            {
                mappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                _extraHandlerMappings[targetConfigPath] = mappings;
            }

            mappings[key] = appId;
        }
    }

    public void RemoveHandlerMapping(string key, AppDatabase database)
    {
        if (database.Settings.HandlerMappings != null)
        {
            database.Settings.HandlerMappings.Remove(key);
            if (database.Settings.HandlerMappings.Count == 0)
                database.Settings.HandlerMappings = null;
        }

        foreach (var mappings in _extraHandlerMappings.Values)
            mappings.Remove(key);
    }

    public Dictionary<string, string>? GetHandlerMappingsForConfig(string configPath)
    {
        var normalized = Path.GetFullPath(configPath);
        if (_extraHandlerMappings.TryGetValue(normalized, out var mappings) && mappings.Count > 0)
            return mappings;
        return null;
    }

    /// <summary>
    /// Registers handler mappings from a loaded extra config.
    /// Called by <see cref="AppConfigService"/> when loading an additional config.
    /// </summary>
    public void RegisterConfigMappings(string configPath, Dictionary<string, string> mappings)
    {
        var normalized = Path.GetFullPath(configPath);
        _extraHandlerMappings[normalized] = new Dictionary<string, string>(mappings, StringComparer.OrdinalIgnoreCase);
        if (!_loadedPaths.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            _loadedPaths.Add(normalized);
    }

    /// <summary>
    /// Removes handler mappings and app-config assignments for an unloaded extra config.
    /// Called by <see cref="AppConfigService"/> when unloading an additional config.
    /// </summary>
    public void UnregisterConfigMappings(string configPath)
    {
        var normalized = Path.GetFullPath(configPath);
        _extraHandlerMappings.Remove(normalized);
        _loadedPaths.RemoveAll(p => string.Equals(p, normalized, StringComparison.OrdinalIgnoreCase));

        var appsToRemove = _appConfigMap
            .Where(kv => string.Equals(kv.Value, normalized, StringComparison.OrdinalIgnoreCase))
            .Select(kv => kv.Key)
            .ToList();
        foreach (var appId in appsToRemove)
            _appConfigMap.Remove(appId);
    }
}