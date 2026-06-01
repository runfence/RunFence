using System.Text.Json.Serialization;

namespace RunFence.Core;

public sealed record AppxLaunchResultPayload(
    [property: JsonPropertyName("ok")] bool Ok,
    [property: JsonPropertyName("stage")] string Stage,
    [property: JsonPropertyName("exitCode")] int ExitCode,
    [property: JsonPropertyName("hresult")] string? HResult,
    [property: JsonPropertyName("message")] string? Message,
    [property: JsonPropertyName("appxExecutablePath")] string? AppxExecutablePath,
    [property: JsonPropertyName("arguments")] string? Arguments);
