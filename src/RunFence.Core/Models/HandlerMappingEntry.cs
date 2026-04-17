using System.Text.Json.Serialization;
using RunFence.Core;

namespace RunFence.Core.Models;

[JsonConverter(typeof(HandlerMappingEntryConverter))]
public record struct HandlerMappingEntry(string AppId, string? ArgumentsTemplate = null);
