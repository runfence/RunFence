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
        List<string>? pathPrefixes = null;
        bool replacePrefixes = false;

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
            else if (string.Equals(propertyName, "PathPrefixes", StringComparison.OrdinalIgnoreCase))
            {
                pathPrefixes = [];
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                {
                    if (reader.TokenType == JsonTokenType.Null)
                        throw new JsonException("PathPrefixes elements must not be null");
                    pathPrefixes.Add(reader.GetString()!);
                }
            }
            else if (string.Equals(propertyName, "ReplacePrefixes", StringComparison.OrdinalIgnoreCase))
                replacePrefixes = reader.GetBoolean();
            else
                reader.Skip();
        }

        if (appId == null)
            throw new JsonException("HandlerMappingEntry requires AppId");

        return new HandlerMappingEntry(appId, argsTemplate, pathPrefixes, replacePrefixes);
    }

    public override void Write(Utf8JsonWriter writer, HandlerMappingEntry value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("AppId", value.AppId);
        if (value.ArgumentsTemplate != null)
            writer.WriteString("ArgumentsTemplate", value.ArgumentsTemplate);
        if (value.PathPrefixes is { Count: > 0 })
        {
            writer.WritePropertyName("PathPrefixes");
            writer.WriteStartArray();
            foreach (var p in value.PathPrefixes) writer.WriteStringValue(p);
            writer.WriteEndArray();
        }
        if (value.ReplacePrefixes)
            writer.WriteBoolean("ReplacePrefixes", true);
        writer.WriteEndObject();
    }
}
