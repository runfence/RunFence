using System.Text.Json;
using System.Text.Json.Serialization;
using PrefTrans.Services;
using PrefTrans.Settings;
using Xunit;

namespace RunFence.Tests;

public class EnvironmentSettingsIOTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    [Fact]
    public void SettingsIoCatalog_ExcludesEnvironmentTransfer()
    {
        var allIo = SettingsIoCatalog.CreateAll(new SafeExecutor(), new NoOpBroadcastHelper(), new UserProfileFilter());

        Assert.DoesNotContain(allIo, io => io.GetType().Name.Contains("EnvironmentSettingsIO", StringComparison.Ordinal));
    }

    [Fact]
    public void StoreAndShowSerialization_DoNotEmitEnvironmentBlock()
    {
        var json = JsonSerializer.Serialize(new UserSettings(), JsonOptions);

        Assert.DoesNotContain("\"environment\"", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Load_IgnoresLegacyEnvironmentPayload()
    {
        const string json = """
                            {
                              "mouse": { "speed": 10 },
                              "environment": {
                                "variables": {
                                  "SECRET_TOKEN": { "value": "abc123", "kind": "SZ" }
                                }
                              }
                            }
                            """;

        var settings = JsonSerializer.Deserialize<UserSettings>(json, JsonOptions);

        Assert.NotNull(settings);
        Assert.NotNull(settings!.Mouse);
        Assert.DoesNotContain("\"environment\"", JsonSerializer.Serialize(settings, JsonOptions), StringComparison.OrdinalIgnoreCase);
    }

    private sealed class NoOpBroadcastHelper : global::PrefTrans.Services.IO.IBroadcastHelper
    {
        public void Broadcast()
        {
        }

        public void BroadcastIntl()
        {
        }
    }
}
