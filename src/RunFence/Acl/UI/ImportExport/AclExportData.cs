using System.Text.Json.Serialization;

namespace RunFence.Acl.UI.ImportExport;

public sealed record AclExportData(
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("grants")] List<AclExportGrantEntry>? Grants,
    [property: JsonPropertyName("traverse")] List<AclExportTraverseEntry>? Traverse);
