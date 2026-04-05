using RunFence.Core.Models;

namespace RunFence.Persistence;

/// <summary>
/// Manages the mapping from handler keys (file extensions / URL protocols) to app IDs,
/// merging across the main config and any loaded additional configs.
/// </summary>
public interface IHandlerMappingService
{
    /// <summary>
    /// Returns the merged effective handler mappings: main config as base, each loaded extra config
    /// overlaying on duplicate keys (in load order, last loaded wins).
    /// </summary>
    Dictionary<string, string> GetEffectiveHandlerMappings(AppDatabase database);

    /// <summary>
    /// Assigns <paramref name="key"/> → <paramref name="appId"/> to the config that owns the app.
    /// If the key currently exists in a different config, it is removed from there first.
    /// </summary>
    void SetHandlerMapping(string key, string appId, AppDatabase database);

    /// <summary>Removes <paramref name="key"/> from whichever config currently owns it.</summary>
    void RemoveHandlerMapping(string key, AppDatabase database);

    /// <summary>
    /// Returns the HandlerMappings dictionary for a loaded extra config (by normalized path),
    /// or null if the config has no mappings or is not loaded.
    /// Used by save helpers to persist mappings into the extra config file.
    /// </summary>
    Dictionary<string, string>? GetHandlerMappingsForConfig(string configPath);

    /// <summary>
    /// Tracks which config file owns <paramref name="appId"/> so that
    /// <see cref="SetHandlerMapping"/> routes to the correct config.
    /// Called by <see cref="AppConfigService"/> whenever an app assignment changes.
    /// </summary>
    void SetAppConfig(string appId, string? configPath);

    /// <summary>
    /// Registers handler mappings from a loaded extra config. Called by AppConfigService on load.
    /// </summary>
    void RegisterConfigMappings(string configPath, Dictionary<string, string> mappings);

    /// <summary>
    /// Removes handler mappings and app-config assignments for an unloaded extra config.
    /// Called by AppConfigService on unload.
    /// </summary>
    void UnregisterConfigMappings(string configPath);
}