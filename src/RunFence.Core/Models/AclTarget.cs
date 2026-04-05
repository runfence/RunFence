using System.Text.Json;
using System.Text.Json.Serialization;

namespace RunFence.Core.Models;

public enum AclTarget
{
    Folder = 0,
    File = 1
}

/// <summary>
/// Reads both "Exe" (legacy) and "File" (current); always writes "File".
/// </summary>
public class AclTargetConverter : JsonConverter<AclTarget>
{
    public override AclTarget Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString();
        return value switch
        {
            "Exe" or "File" => AclTarget.File,
            _ => AclTarget.Folder
        };
    }

    public override void Write(Utf8JsonWriter writer, AclTarget value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString());
}