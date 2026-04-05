using System.Text.Json;
using System.Text.Json.Serialization;

namespace RunFence.Core;

/// <summary>
/// Reads both the old V1 format <c>[{"sid":"S-1-5-..."}]</c> and the new V2 format
/// <c>["S-1-5-..."]</c>; always writes V2 (plain strings).
/// Applied to <see cref="RunFence.Core.Models.AppEntry.AllowedIpcCallers"/> so old
/// AppConfig (.rfn) files continue loading correctly.
/// </summary>
public class SidStringListConverter : JsonConverter<List<string>?>
{
    public override List<string>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;

        if (reader.TokenType != JsonTokenType.StartArray)
            throw new JsonException("Expected StartArray for SID list.");

        var result = new List<string>();
        while (reader.Read())
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.EndArray:
                    return result;
                case JsonTokenType.String:
                {
                    // V2 format: plain string
                    var s = reader.GetString();
                    if (!string.IsNullOrEmpty(s))
                        result.Add(s);
                    break;
                }
                case JsonTokenType.StartObject:
                {
                    // V1 format: {"sid":"..."}
                    string? sid = null;
                    while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                    {
                        if (reader.TokenType == JsonTokenType.PropertyName &&
                            string.Equals(reader.GetString(), "sid", StringComparison.OrdinalIgnoreCase))
                        {
                            reader.Read();
                            sid = reader.GetString();
                        }
                        else
                        {
                            reader.Skip();
                        }
                    }

                    if (!string.IsNullOrEmpty(sid))
                        result.Add(sid);
                    break;
                }
                default:
                    reader.Skip();
                    break;
            }
        }

        throw new JsonException("Unexpected end of JSON for SID list.");
    }

    public override void Write(Utf8JsonWriter writer, List<string>? value, JsonSerializerOptions options)
    {
        if (value == null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartArray();
        foreach (var sid in value)
            writer.WriteStringValue(sid);
        writer.WriteEndArray();
    }
}