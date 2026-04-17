using System.Text.Json;
using System.Text.Json.Serialization;
using RunFence.Core.Models;

namespace RunFence.Core;

public class HandlerMappingEntryConverter : JsonConverter<HandlerMappingEntry>
{
    public override HandlerMappingEntry Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
            return new HandlerMappingEntry(reader.GetString()!);

        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException($"Expected string or object for HandlerMappingEntry, got {reader.TokenType}");

        string? appId = null;
        string? argsTemplate = null;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                break;

            if (reader.TokenType != JsonTokenType.PropertyName)
                throw new JsonException("Expected property name");

            var propertyName = reader.GetString();
            reader.Read();

            if (string.Equals(propertyName, "AppId", StringComparison.OrdinalIgnoreCase))
                appId = reader.GetString();
            else if (string.Equals(propertyName, "ArgumentsTemplate", StringComparison.OrdinalIgnoreCase))
                argsTemplate = reader.TokenType == JsonTokenType.Null ? null : reader.GetString();
            else
                reader.Skip();
        }

        if (appId == null)
            throw new JsonException("HandlerMappingEntry requires AppId");

        return new HandlerMappingEntry(appId, argsTemplate);
    }

    public override void Write(Utf8JsonWriter writer, HandlerMappingEntry value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("AppId", value.AppId);
        if (value.ArgumentsTemplate != null)
            writer.WriteString("ArgumentsTemplate", value.ArgumentsTemplate);
        writer.WriteEndObject();
    }
}
