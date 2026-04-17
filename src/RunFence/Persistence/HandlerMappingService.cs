using RunFence.Core.Models;

namespace RunFence.Persistence;

/// <summary>
/// Manages handler mappings (extension/protocol → app entry) across the main config and loaded
/// additional configs. Extra config mappings are held in memory; main config mappings live in
/// <see cref="AppDatabase"/>.
/// <para>
/// App-to-config assignments and loaded-path ordering are owned by <see cref="AppConfigIndex"/>,
/// which is injected as the authoritative source and updated by <see cref="AppConfigService"/> directly.
/// </para>
/// </summary>
public class HandlerMappingService(AppConfigIndex appConfigIndex) : IHandlerMappingService
{
    // normalized config path → HandlerMappings for that extra config, in registration order
    private readonly Dictionary<string, Dictionary<string, HandlerMappingEntry>> _extraHandlerMappings =
        new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, HandlerMappingEntry> GetEffectiveHandlerMappings(AppDatabase database)
    {
        var result = database.Settings.HandlerMappings != null
            ? new Dictionary<string, HandlerMappingEntry>(database.Settings.HandlerMappings, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, HandlerMappingEntry>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in appConfigIndex.GetLoadedConfigPaths())
        {
            if (_extraHandlerMappings.TryGetValue(path, out var extraMappings))
            {
                foreach (var kvp in extraMappings)
                    result[kvp.Key] = kvp.Value;
            }
        }

        return result;
    }

    public IReadOnlyDictionary<string, IReadOnlyList<HandlerMappingEntry>> GetAllHandlerMappings(AppDatabase database)
    {
        var result = new Dictionary<string, List<HandlerMappingEntry>>(StringComparer.OrdinalIgnoreCase);

        if (database.Settings.HandlerMappings != null)
        {
            foreach (var kvp in database.Settings.HandlerMappings)
            {
                if (!result.TryGetValue(kvp.Key, out var list))
                    result[kvp.Key] = list = [];
                list.Add(kvp.Value);
            }
        }

        foreach (var path in appConfigIndex.GetLoadedConfigPaths())
        {
            if (_extraHandlerMappings.TryGetValue(path, out var extraMappings))
            {
                foreach (var kvp in extraMappings)
                {
                    if (!result.TryGetValue(kvp.Key, out var list))
                        result[kvp.Key] = list = [];
                    list.Add(kvp.Value);
                }
            }
        }

        return result.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyList<HandlerMappingEntry>)kv.Value,
            StringComparer.OrdinalIgnoreCase);
    }

    public void SetHandlerMapping(string key, HandlerMappingEntry entry, AppDatabase database)
    {
        // Only remove from the config that owns this appId — preserve other configs' mappings for the same key
        RemoveHandlerMapping(key, entry.AppId, database);

        var targetConfigPath = appConfigIndex.GetConfigPath(entry.AppId);

        if (targetConfigPath == null)
        {
            database.Settings.HandlerMappings ??= new Dictionary<string, HandlerMappingEntry>(StringComparer.OrdinalIgnoreCase);
            database.Settings.HandlerMappings[key] = entry;
        }
        else
        {
            if (!_extraHandlerMappings.TryGetValue(targetConfigPath, out var mappings))
            {
                mappings = new Dictionary<string, HandlerMappingEntry>(StringComparer.OrdinalIgnoreCase);
                _extraHandlerMappings[targetConfigPath] = mappings;
            }

            mappings[key] = entry;
        }
    }

    public void RemoveHandlerMapping(string key, string appId, AppDatabase database)
    {
        var configPath = appConfigIndex.GetConfigPath(appId);

        if (configPath == null)
        {
            // App is in main config — only remove if the mapped entry's AppId is actually appId
            if (database.Settings.HandlerMappings != null &&
                database.Settings.HandlerMappings.TryGetValue(key, out var existingEntry) &&
                string.Equals(existingEntry.AppId, appId, StringComparison.OrdinalIgnoreCase))
            {
                database.Settings.HandlerMappings.Remove(key);
                if (database.Settings.HandlerMappings.Count == 0)
                    database.Settings.HandlerMappings = null;
            }
        }
        else
        {
            // App is in an extra config — only remove if the mapped entry's AppId is actually appId
            if (_extraHandlerMappings.TryGetValue(configPath, out var mappings) &&
                mappings.TryGetValue(key, out var existingEntry) &&
                string.Equals(existingEntry.AppId, appId, StringComparison.OrdinalIgnoreCase))
                mappings.Remove(key);
        }
    }

    public Dictionary<string, HandlerMappingEntry>? GetHandlerMappingsForConfig(string configPath)
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
    public void RegisterConfigMappings(string configPath, Dictionary<string, HandlerMappingEntry> mappings)
    {
        var normalized = Path.GetFullPath(configPath);
        _extraHandlerMappings[normalized] = new Dictionary<string, HandlerMappingEntry>(mappings, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Removes handler mappings for an unloaded extra config.
    /// Called by <see cref="AppConfigService"/> when unloading an additional config.
    /// </summary>
    public void UnregisterConfigMappings(string configPath)
    {
        var normalized = Path.GetFullPath(configPath);
        _extraHandlerMappings.Remove(normalized);
    }

    public Dictionary<string, DirectHandlerEntry> GetEffectiveDirectHandlerMappings(AppDatabase database)
        => database.Settings.DirectHandlerMappings != null
            ? new Dictionary<string, DirectHandlerEntry>(database.Settings.DirectHandlerMappings, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, DirectHandlerEntry>(StringComparer.OrdinalIgnoreCase);

    public void SetDirectHandlerMapping(string key, DirectHandlerEntry entry, AppDatabase database)
    {
        // Remove any conflicting main-config app mapping for the same key
        if (database.Settings.HandlerMappings != null &&
            database.Settings.HandlerMappings.TryGetValue(key, out var conflictingEntry))
        {
            RemoveHandlerMapping(key, conflictingEntry.AppId, database);
        }

        database.Settings.DirectHandlerMappings ??= new Dictionary<string, DirectHandlerEntry>(StringComparer.OrdinalIgnoreCase);
        database.Settings.DirectHandlerMappings[key] = entry;
    }

    public void RemoveDirectHandlerMapping(string key, AppDatabase database)
    {
        if (database.Settings.DirectHandlerMappings == null)
            return;

        database.Settings.DirectHandlerMappings.Remove(key);

        if (database.Settings.DirectHandlerMappings.Count == 0)
            database.Settings.DirectHandlerMappings = null;
    }
}
