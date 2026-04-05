using System.Text.Json.Serialization;

namespace RunFence.Core.Models;

public class AppContainerEntry
{
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Capabilities { get; set; }

    public bool EnableLoopback { get; set; }
    public bool IsEphemeral { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTime? DeleteAfterUtc { get; set; }

    /// <summary>
    /// CLSIDs granted COM launch+access permissions (HKCR\AppID\{clsid}).
    /// Null when no COM grants configured; empty list treated the same as null on load.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? ComAccessClsids { get; set; }
}