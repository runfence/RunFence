using System.Text.Json.Serialization;

namespace RunFence.Core.Models;

[JsonConverter(typeof(HandlerMappingEntryConverter))]
public record struct HandlerMappingEntry(
    string AppId,
    string? ArgumentsTemplate = null,
    List<string>? PathPrefixes = null,
    bool ReplacePrefixes = false);
