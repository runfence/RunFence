using System.Text.Json.Serialization;

namespace RunFence.Acl.UI.ImportExport;

public sealed record AclExportTraverseEntry(
    [property: JsonPropertyName("path")] string Path);
