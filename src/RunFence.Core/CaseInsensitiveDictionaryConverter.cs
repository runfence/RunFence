using System.Text.Json;
using System.Text.Json.Serialization;

namespace RunFence.Core;

/// <summary>
/// Base converter that deserializes a JSON object into a <see cref="Dictionary{TKey,TValue}"/>
/// with <see cref="StringComparer.OrdinalIgnoreCase"/> keys. Subclasses may override
/// <see cref="ShouldSkipEntry"/> to omit specific values during serialization.
/// </summary>
public abstract class CaseInsensitiveDictionaryConverter<TValue> : JsonConverter<Dictionary<string, TValue>>
{
    protected abstract string ExpectedObjectLabel { get; }

    public override Dictionary<string, TValue> Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException($"Expected StartObject token for {ExpectedObjectLabel}.");

        var dict = new Dictionary<string, TValue>(StringComparer.OrdinalIgnoreCase);
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                return dict;

            var key = reader.GetString()!;
            reader.Read();
            var value = JsonSerializer.Deserialize<TValue>(ref reader, options);
            if (value != null)
                dict[key] = value;
        }

        throw new JsonException($"Unexpected end of JSON for {ExpectedObjectLabel}.");
    }

    public override void Write(
        Utf8JsonWriter writer, Dictionary<string, TValue> value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach (var kvp in value)
        {
            if (ShouldSkipEntry(kvp.Value))
                continue;
            writer.WritePropertyName(kvp.Key);
            JsonSerializer.Serialize(writer, kvp.Value, options);
        }

        writer.WriteEndObject();
    }

    /// <summary>
    /// Returns true if the entry should be omitted from the serialized output.
    /// Default: never skip.
    /// </summary>
    protected bool ShouldSkipEntry(TValue value) => false;
}