using System.Text.Json;
using RunFence.Core;
using Xunit;

namespace RunFence.Tests;

public class SidNamesDictionaryConverterTests
{
    private static Dictionary<string, string>? Deserialize(string json)
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new SidNamesDictionaryConverter());
        return JsonSerializer.Deserialize<Dictionary<string, string>>(json, options);
    }

    private static string Serialize(Dictionary<string, string> dict)
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new SidNamesDictionaryConverter());
        return JsonSerializer.Serialize(dict, options);
    }

    [Fact]
    public void Deserialize_PopulatesEntries()
    {
        var json = """{"S-1-5-21-1-1-1-1001": "alice", "S-1-5-21-1-1-1-1002": "bob"}""";
        var result = Deserialize(json);
        Assert.NotNull(result);
        Assert.Equal("alice", result["S-1-5-21-1-1-1-1001"]);
        Assert.Equal("bob", result["S-1-5-21-1-1-1-1002"]);
    }

    [Fact]
    public void Deserialize_IsCaseInsensitive()
    {
        var json = """{"S-1-5-21-1-1-1-1001": "alice"}""";
        var result = Deserialize(json);
        Assert.NotNull(result);
        Assert.Equal("alice", result["s-1-5-21-1-1-1-1001"]);
        Assert.Equal("alice", result["S-1-5-21-1-1-1-1001"]);
    }

    [Fact]
    public void Deserialize_EmptyObject_ReturnsEmptyDict()
    {
        var result = Deserialize("{}");
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public void Deserialize_NullValue_SkipsEntry()
    {
        var json = """{"S-1-5-21-1-1-1-1001": null, "S-1-5-21-1-1-1-1002": "bob"}""";
        var result = Deserialize(json);
        Assert.NotNull(result);
        Assert.False(result.ContainsKey("S-1-5-21-1-1-1-1001"));
        Assert.Equal("bob", result["S-1-5-21-1-1-1-1002"]);
    }

    [Fact]
    public void Deserialize_InvalidToken_ThrowsJsonException()
    {
        Assert.Throws<JsonException>(() => Deserialize("[1,2,3]"));
    }

    [Fact]
    public void RoundTrip_PreservesAllEntries()
    {
        var original = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["S-1-5-21-1-1-1-1001"] = "alice",
            ["S-1-5-21-1-1-1-1002"] = "bob"
        };
        var json = Serialize(original);
        var restored = Deserialize(json);
        Assert.NotNull(restored);
        Assert.Equal("alice", restored["S-1-5-21-1-1-1-1001"]);
        Assert.Equal("bob", restored["S-1-5-21-1-1-1-1002"]);
    }
}