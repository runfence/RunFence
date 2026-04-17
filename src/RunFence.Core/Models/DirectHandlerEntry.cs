using System.Text.Json.Serialization;

namespace RunFence.Core.Models;

/// <summary>
/// A direct handler association — maps to a raw command or HKLM class name instead of an AppEntry.
/// Exactly one of <see cref="Command"/> or <see cref="ClassName"/> must be non-null.
/// <see cref="ClassName"/> is valid for file extensions only; protocols must use <see cref="Command"/>.
/// </summary>
public record struct DirectHandlerEntry
{
    /// <summary>Raw command line, e.g., <c>"C:\notepad.exe" "%1"</c>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Command { get; init; }

    /// <summary>HKLM ProgId class name, e.g., <c>txtfile</c>. Extensions only, not protocols.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ClassName { get; init; }
}
