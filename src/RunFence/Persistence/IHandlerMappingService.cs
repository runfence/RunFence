using RunFence.Core.Models;

namespace RunFence.Persistence;

/// <summary>
/// Manages the mapping from handler keys (file extensions / URL protocols) to app entries,
/// merging across the main config and any loaded additional configs.
/// Also manages direct handler mappings stored in the main config only.
/// </summary>
public interface IHandlerMappingService
{
    /// <summary>
    /// Returns the merged effective handler mappings: main config as base, each loaded extra config
    /// overlaying on duplicate keys (in load order, last loaded wins).
    /// Used for registry sync where a single app per key is required.
    /// </summary>
    Dictionary<string, HandlerMappingEntry> GetEffectiveHandlerMappings(AppDatabase database);

    /// <summary>
    /// Returns all handler mappings across all configs without deduplication.
    /// When multiple configs define the same key, all entries are returned (main config first, then
    /// extra configs in load order). Used for IPC resolution and UI display.
    /// </summary>
    IReadOnlyDictionary<string, IReadOnlyList<HandlerMappingEntry>> GetAllHandlerMappings(AppDatabase database);

    /// <summary>
    /// Assigns <paramref name="key"/> → <paramref name="entry"/> to the config that owns the app.
    /// Only modifies that config — other configs' mappings for the same key are preserved.
    /// </summary>
    void SetHandlerMapping(string key, HandlerMappingEntry entry, AppDatabase database);

    /// <summary>
    /// Removes the mapping for <paramref name="key"/> only from the config that owns <paramref name="appId"/>.
    /// Other configs' mappings for the same key are preserved.
    /// </summary>
    void RemoveHandlerMapping(string key, string appId, AppDatabase database);

    /// <summary>
    /// Returns the HandlerMappings dictionary for a loaded extra config (by normalized path),
    /// or null if the config has no mappings or is not loaded.
    /// Used by save helpers to persist mappings into the extra config file.
    /// </summary>
    Dictionary<string, HandlerMappingEntry>? GetHandlerMappingsForConfig(string configPath);

    /// <summary>
    /// Registers handler mappings from a loaded extra config. Called by AppConfigService on load.
    /// </summary>
    void RegisterConfigMappings(string configPath, Dictionary<string, HandlerMappingEntry> mappings);

    /// <summary>
    /// Removes handler mappings for an unloaded extra config.
    /// Called by AppConfigService on unload.
    /// </summary>
    void UnregisterConfigMappings(string configPath);

    /// <summary>
    /// Returns the direct handler mappings from the main config (or empty if none).
    /// </summary>
    Dictionary<string, DirectHandlerEntry> GetEffectiveDirectHandlerMappings(AppDatabase database);

    /// <summary>
    /// Stores a direct handler for <paramref name="key"/> in the main config.
    /// Also removes any conflicting main-config app mapping for the same key.
    /// </summary>
    void SetDirectHandlerMapping(string key, DirectHandlerEntry entry, AppDatabase database);

    /// <summary>
    /// Removes the direct handler mapping for <paramref name="key"/> from the main config.
    /// Sets <see cref="AppSettings.DirectHandlerMappings"/> to null when it becomes empty.
    /// </summary>
    void RemoveDirectHandlerMapping(string key, AppDatabase database);
}
