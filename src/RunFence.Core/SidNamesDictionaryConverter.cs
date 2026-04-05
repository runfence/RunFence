using System.Text.Json;

namespace RunFence.Core;

/// <summary>
/// Deserializes a JSON object into a Dictionary&lt;string, string&gt; with
/// StringComparer.OrdinalIgnoreCase so SID lookups are case-insensitive.
/// Uses direct string reading/writing for efficiency (no intermediate serialization).
/// </summary>
public class SidNamesDictionaryConverter : CaseInsensitiveDictionaryConverter<string>
{
    protected override string ExpectedObjectLabel => "SidNames";

    public override Dictionary<string, string> Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException($"Expected StartObject token for {ExpectedObjectLabel}.");

        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                return dict;

            var key = reader.GetString()!;
            reader.Read();
            var value = reader.GetString();
            if (value == null)
                continue;
            dict[key] = value;
        }

        throw new JsonException($"Unexpected end of JSON for {ExpectedObjectLabel}.");
    }

    public override void Write(
        Utf8JsonWriter writer, Dictionary<string, string> value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach (var kvp in value)
            writer.WriteString(kvp.Key, kvp.Value);
        writer.WriteEndObject();
    }
}