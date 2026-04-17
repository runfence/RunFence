using System.Text.Json;
using RunFence.Core.Models;
using Xunit;

namespace RunFence.Tests;

public class HandlerMappingEntryConverterTests
{
    private static readonly JsonSerializerOptions Options = new();

    [Fact]
    public void Deserialize_PlainString_ReturnsEntryWithNullTemplate()
    {
        // Old format: bare string value (backward compatibility)
        var json = "\"myAppId\"";

        var entry = JsonSerializer.Deserialize<HandlerMappingEntry>(json, Options);

        Assert.Equal("myAppId", entry.AppId);
        Assert.Null(entry.ArgumentsTemplate);
    }

    [Fact]
    public void Deserialize_ObjectWithNoTemplate_ReturnsEntryWithNullTemplate()
    {
        var json = """{"AppId":"myAppId"}""";

        var entry = JsonSerializer.Deserialize<HandlerMappingEntry>(json, Options);

        Assert.Equal("myAppId", entry.AppId);
        Assert.Null(entry.ArgumentsTemplate);
    }

    [Fact]
    public void Deserialize_ObjectWithTemplate_ReturnsEntryWithTemplate()
    {
        var json = """{"AppId":"myAppId","ArgumentsTemplate":"\"%1\""}""";

        var entry = JsonSerializer.Deserialize<HandlerMappingEntry>(json, Options);

        Assert.Equal("myAppId", entry.AppId);
        Assert.Equal("\"%1\"", entry.ArgumentsTemplate);
    }

    [Fact]
    public void Serialize_NullTemplate_OmitsArgumentsTemplateProperty()
    {
        var entry = new HandlerMappingEntry("myAppId");

        var json = JsonSerializer.Serialize(entry, Options);

        Assert.Contains("myAppId", json);
        Assert.DoesNotContain("ArgumentsTemplate", json);
    }

    [Fact]
    public void Serialize_WithTemplate_IncludesArgumentsTemplate()
    {
        var entry = new HandlerMappingEntry("myAppId", "\"%1\"");

        var json = JsonSerializer.Serialize(entry, Options);

        Assert.Contains("myAppId", json);
        Assert.Contains("ArgumentsTemplate", json);
    }

    [Fact]
    public void RoundTrip_WithTemplate_PreservesAllFields()
    {
        var original = new HandlerMappingEntry("myAppId", "--flag \"%1\"");

        var json = JsonSerializer.Serialize(original, Options);
        var deserialized = JsonSerializer.Deserialize<HandlerMappingEntry>(json, Options);

        Assert.Equal(original.AppId, deserialized.AppId);
        Assert.Equal(original.ArgumentsTemplate, deserialized.ArgumentsTemplate);
    }

    [Fact]
    public void Deserialize_MissingAppId_ThrowsJsonException()
    {
        var json = """{"ArgumentsTemplate":"\"%1\""}""";

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<HandlerMappingEntry>(json, Options));
    }

    [Fact]
    public void Deserialize_Dictionary_MixedOldAndNewFormat_ReturnsCorrectEntries()
    {
        // A dictionary where some values are plain strings (old format) and some are objects (new format).
        // This exercises backward compatibility when config files contain a mix of both.
        var json = """
            {
                ".txt": "plainAppId",
                ".log": {"AppId":"objectAppId"},
                ".csv": {"AppId":"templateAppId","ArgumentsTemplate":"\"%1\""}
            }
            """;

        var dict = JsonSerializer.Deserialize<Dictionary<string, HandlerMappingEntry>>(json, Options);

        Assert.NotNull(dict);
        Assert.Equal(3, dict!.Count);

        // Old format: plain string value
        Assert.Equal("plainAppId", dict[".txt"].AppId);
        Assert.Null(dict[".txt"].ArgumentsTemplate);

        // New format: object without template
        Assert.Equal("objectAppId", dict[".log"].AppId);
        Assert.Null(dict[".log"].ArgumentsTemplate);

        // New format: object with template
        Assert.Equal("templateAppId", dict[".csv"].AppId);
        Assert.Equal("\"%1\"", dict[".csv"].ArgumentsTemplate);
    }
}
