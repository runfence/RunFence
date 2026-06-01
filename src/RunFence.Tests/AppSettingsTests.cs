using System.Text.Json;
using RunFence.Core;
using RunFence.Core.Models;
using Xunit;

namespace RunFence.Tests;

public class AppSettingsTests
{
    [Fact]
    public void Deserialize_UnknownEnableLoggingProperty_UsesDefaultLogVerbosity()
    {
        var settings = JsonSerializer.Deserialize<AppSettings>(
            """{"enableLogging":false}""",
            JsonDefaults.Options)!;

        Assert.Equal(LogVerbosity.Info, settings.LogVerbosity);
    }

    [Fact]
    public void Serialize_WritesLogVerbosityAndOmitsLegacyEnableLogging()
    {
        var settings = new AppSettings { LogVerbosity = LogVerbosity.Debug };

        var json = JsonSerializer.Serialize(settings, JsonDefaults.Options);

        Assert.Contains("logVerbosity", json);
        Assert.DoesNotContain("enableLogging", json);
    }

    [Fact]
    public void Deserialize_LogVerbosityProperty_UsesConfiguredValue()
    {
        var settings = JsonSerializer.Deserialize<AppSettings>(
            """{"logVerbosity":"Error"}""",
            JsonDefaults.Options)!;

        Assert.Equal(LogVerbosity.Error, settings.LogVerbosity);
    }

}
