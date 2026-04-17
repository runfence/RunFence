using System.Text.Json;
using RunFence.Core.Models;
using Xunit;

namespace RunFence.Tests;

public class DirectHandlerEntrySerializationTests
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    [Fact]
    public void Command_SerializesAndDeserializesCorrectly()
    {
        var entry = new DirectHandlerEntry { Command = @"""C:\notepad.exe"" ""%1""" };

        var json = JsonSerializer.Serialize(entry, Options);
        var restored = JsonSerializer.Deserialize<DirectHandlerEntry>(json, Options);

        Assert.Equal(entry.Command, restored.Command);
        Assert.Null(restored.ClassName);
    }

    [Fact]
    public void ClassName_SerializesAndDeserializesCorrectly()
    {
        var entry = new DirectHandlerEntry { ClassName = "txtfile" };

        var json = JsonSerializer.Serialize(entry, Options);
        var restored = JsonSerializer.Deserialize<DirectHandlerEntry>(json, Options);

        Assert.Equal(entry.ClassName, restored.ClassName);
        Assert.Null(restored.Command);
    }

    [Fact]
    public void NullFields_OmittedFromJson()
    {
        var commandOnly = new DirectHandlerEntry { Command = "cmd.exe" };
        var classOnly = new DirectHandlerEntry { ClassName = "txtfile" };

        var jsonCommand = JsonSerializer.Serialize(commandOnly, Options);
        var jsonClass = JsonSerializer.Serialize(classOnly, Options);

        Assert.DoesNotContain("ClassName", jsonCommand);
        Assert.DoesNotContain("Command", jsonClass);
    }

    [Fact]
    public void AppSettings_DirectHandlerMappings_OmittedWhenNull()
    {
        var settings = new AppSettings();
        var json = JsonSerializer.Serialize(settings, Options);

        Assert.DoesNotContain("DirectHandlerMappings", json);
    }

    [Fact]
    public void AppSettings_Clone_DeepCopiesDirectHandlerMappings()
    {
        var settings = new AppSettings
        {
            DirectHandlerMappings = new Dictionary<string, DirectHandlerEntry>(StringComparer.OrdinalIgnoreCase)
            {
                [".txt"] = new DirectHandlerEntry { ClassName = "txtfile" },
                [".py"] = new DirectHandlerEntry { Command = @"""C:\python.exe"" ""%1""" }
            }
        };

        var clone = settings.Clone();

        Assert.NotNull(clone.DirectHandlerMappings);
        Assert.Equal(2, clone.DirectHandlerMappings.Count);
        Assert.Equal("txtfile", clone.DirectHandlerMappings[".txt"].ClassName);
        Assert.Equal(@"""C:\python.exe"" ""%1""", clone.DirectHandlerMappings[".py"].Command);

        // Verify deep copy — modifying clone does not affect original
        clone.DirectHandlerMappings[".txt"] = new DirectHandlerEntry { Command = "changed" };
        Assert.Equal("txtfile", settings.DirectHandlerMappings[".txt"].ClassName);
    }
}
