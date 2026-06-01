using System.Text.Json.Serialization;

namespace RunFence.Acl.UI.ImportExport;

public sealed record AclExportGrantEntry(
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("isDeny")] bool IsDeny,
    [property: JsonPropertyName("execute")] bool Execute,
    [property: JsonPropertyName("write")] bool Write,
    [property: JsonPropertyName("read")] bool Read,
    [property: JsonPropertyName("special")] bool Special,
    [property: JsonPropertyName("owner")] bool Owner);
