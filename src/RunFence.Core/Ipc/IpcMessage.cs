using System.Text.Json.Serialization;

namespace RunFence.Core.Ipc;

public class IpcMessage
{
    public string Command { get; set; } = string.Empty;
    public string? AppId { get; set; }
    public string? Arguments { get; set; }
    public string? WorkingDirectory { get; set; }

    /// <summary>
    /// For <see cref="IpcCommands.HandleAssociation"/>: the association key (e.g., "http", ".pdf").
    /// The path/URL to open is stored in <see cref="Arguments"/>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Association { get; set; }

    /// <summary>
    /// Optional: the caller's username, included as a fallback for name-based authorization
    /// when pipe impersonation fails on the server side and SID resolution is unavailable.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CallerName { get; set; }
}