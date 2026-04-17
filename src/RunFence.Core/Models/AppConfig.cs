using System.Text.Json.Serialization;

namespace RunFence.Core.Models;

/// <summary>
/// Model for additional app config files loaded from removable/external media.
/// Each additional config contains a list of AppEntry objects and per-SID grants.
/// </summary>
public class AppConfig
{
    public int Version { get; set; } = 2;
    public List<AppEntry> Apps { get; set; } = new();

    /// <summary>
    /// Per-SID grant entries for this config file.
    /// Mirrors the accounts in AppDatabase but scoped to this config's entries.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<AppConfigAccountEntry>? Accounts { get; set; }

    /// <summary>
    /// Handler associations for this config's apps: extension/protocol → appId.
    /// Keys: file extensions start with '.' (e.g., ".pdf"), URL protocols are bare words (e.g., "http").
    /// Null when empty (omitted from JSON).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, HandlerMappingEntry>? HandlerMappings { get; set; }
}