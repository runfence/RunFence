using System.Text;
using System.Text.Json;
using Moq;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Persistence;
using RunFence.Persistence.UI;
using Xunit;

namespace RunFence.Tests;

public class ConfigExportControllerTests
{
    [Fact]
    public void Export_MainConfig_AppliesFilterAndWritesUtf8JsonWithExpectedMappings()
    {
        var database = new AppDatabase
        {
            Apps =
            [
                new AppEntry { Id = "main-app", Name = "Main", AccountSid = "S-1-5-21-0-0-0-1" },
                new AppEntry { Id = "extra-app", Name = "Extra", AccountSid = "S-1-5-21-0-0-0-2" },
            ],
            Settings = new AppSettings
            {
                HandlerMappings = new Dictionary<string, HandlerMappingEntry>(StringComparer.OrdinalIgnoreCase)
                {
                    [".main"] = new HandlerMappingEntry("wrong-app")
                }
            },
        };

        var session = new SessionContext
        {
            Database = database,
            CredentialStore = new CredentialStore(),
        };
        using var _ = session;

        using var tempDir = new TempDirectory("RunFence_ConfigExport");
        var exportPath = Path.Combine(tempDir.Path, "main-export.json");

        var filteredDb = new AppDatabase
        {
            Apps = [database.Apps[0]],
            Settings = new AppSettings(),
            TrackingJobSids = ["S-1-5-21-1-2-3-1001"],
        };

        var appConfig = new AppConfig
        {
            Apps = [database.Apps[0]],
            Accounts = [],
            HandlerMappings = new Dictionary<string, HandlerMappingEntry>(StringComparer.OrdinalIgnoreCase)
            {
                [".txt"] = new HandlerMappingEntry("main-app", "\"%1\"", ["C:\\Allowed"], ReplacePrefixes: true)
            },
        };

        var fileContentService = new Mock<IFileContentService>();
        string? capturedJson = null;
        Encoding? capturedEncoding = null;
        string? capturedPath = null;
        fileContentService
            .Setup(s => s.WriteAllText(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Encoding>()))
            .Callback<string, string, Encoding>((path, payload, encoding) =>
            {
                capturedPath = path;
                capturedJson = payload;
                capturedEncoding = encoding;
            });

        var appConfigService = new Mock<IAppConfigService>();
        appConfigService.Setup(s => s.GetConfigForExport(null, database)).Returns(appConfig);

        var appFilter = new Mock<IAppFilter>();
        appFilter.Setup(s => s.FilterForMainConfig(database)).Returns(filteredDb);

        var sessionProvider = new Mock<ISessionProvider>();
        sessionProvider.Setup(s => s.GetSession()).Returns(session);

        var log = new Mock<ILoggingService>();

        var controller = new ConfigExportController(
            appConfigService.Object,
            appFilter.Object,
            sessionProvider.Object,
            fileContentService.Object,
            log.Object);

        var result = controller.Export(null, exportPath);

        Assert.True(result.Succeeded);
        Assert.Equal(Path.GetFileName(exportPath), result.ExportedFileName);
        Assert.Null(result.ErrorMessage);
        Assert.NotNull(capturedJson);
        Assert.Same(Encoding.UTF8, capturedEncoding);
        Assert.Equal(exportPath, capturedPath);
        appFilter.Verify(s => s.FilterForMainConfig(database), Times.Once);

        var exported = JsonSerializer.Deserialize<AppDatabase>(capturedJson, JsonDefaults.Options)!;
        Assert.NotNull(exported.Settings);
        Assert.NotNull(exported.Settings.HandlerMappings);
        Assert.False(exported.Settings.HandlerMappings.ContainsKey(".main"));
        Assert.True(exported.Settings.HandlerMappings.ContainsKey(".txt"));
        Assert.Equal(["S-1-5-21-1-2-3-1001"], exported.TrackingJobSids);
        var mapping = exported.Settings.HandlerMappings[".txt"];
        Assert.Equal("main-app", mapping.AppId);
        Assert.Equal("\"%1\"", mapping.ArgumentsTemplate);
        Assert.NotNull(exported.Apps);
        Assert.Single(exported.Apps);
        Assert.Equal("main-app", exported.Apps[0].Id);
    }

    [Fact]
    public void Export_AdditionalConfig_SerializesAdditionalConfigPayload()
    {
        var database = new AppDatabase
        {
            Apps = [new AppEntry { Id = "main-app", Name = "Main", AccountSid = "S-1-5-21-0-0-0-1" }],
        };

        var session = new SessionContext
        {
            Database = database,
            CredentialStore = new CredentialStore(),
        };
        using var _ = session;

        using var tempDir = new TempDirectory("RunFence_ConfigExport");
        var exportPath = Path.Combine(tempDir.Path, "extra-export.json");

        var appConfig = new AppConfig
        {
            Version = 2,
            Apps = [new AppEntry { Id = "extra-app", Name = "Extra" }],
            Accounts =
            [
                new AppConfigAccountEntry
                {
                    Sid = "S-1-5-21-0-0-0-2",
                    Grants = [new GrantedPathEntry { Path = @"C:\ExtraGrant", IsDeny = true }],
                },
            ],
            HandlerMappings = new Dictionary<string, HandlerMappingEntry>(StringComparer.OrdinalIgnoreCase)
            {
                [".cfg"] = new HandlerMappingEntry("extra-app")
            },
        };

        string? capturedJson = null;
        var fileContentService = new Mock<IFileContentService>();
        fileContentService
            .Setup(s => s.WriteAllText(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Encoding>()))
            .Callback<string, string, Encoding>((_, payload, _) => capturedJson = payload);

        var appConfigService = new Mock<IAppConfigService>();
        appConfigService.Setup(s => s.GetConfigForExport(@"C:\extra.rfn", database)).Returns(appConfig);

        var appFilter = new Mock<IAppFilter>();
        var sessionProvider = new Mock<ISessionProvider>();
        sessionProvider.Setup(s => s.GetSession()).Returns(session);

        var controller = new ConfigExportController(
            appConfigService.Object,
            appFilter.Object,
            sessionProvider.Object,
            fileContentService.Object,
            Mock.Of<ILoggingService>());

        var result = controller.Export(@"C:\extra.rfn", exportPath);

        Assert.True(result.Succeeded);
        Assert.Equal(Path.GetFileName(exportPath), result.ExportedFileName);
        Assert.Null(result.ErrorMessage);
        appFilter.Verify(s => s.FilterForMainConfig(database), Times.Never);

        Assert.NotNull(capturedJson);
        var exported = JsonSerializer.Deserialize<AppConfig>(capturedJson, JsonDefaults.Options)!;
        Assert.NotNull(exported.Accounts);
        Assert.Single(exported.Accounts);
        Assert.Equal("extra-app", exported.Apps[0].Id);
        Assert.Equal("C:\\ExtraGrant", exported.Accounts[0].Grants![0].Path);
        Assert.True(exported.Accounts[0].Grants![0].IsDeny);
        Assert.NotNull(exported.HandlerMappings);
        Assert.True(exported.HandlerMappings.ContainsKey(".cfg"));
        Assert.Equal("extra-app", exported.HandlerMappings![".cfg"].AppId);
    }

    [Fact]
    public void Export_WhenWriteFails_ReturnsFailureResult()
    {
        var database = new AppDatabase();
        using var session = new SessionContext
        {
            Database = database,
            CredentialStore = new CredentialStore(),
        };

        using var tempDir = new TempDirectory("RunFence_ConfigExport");
        var exportPath = Path.Combine(tempDir.Path, "main-export.json");

        var fileContentService = new Mock<IFileContentService>();
        fileContentService
            .Setup(s => s.WriteAllText(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Encoding>()))
            .Throws(new IOException("disk full"));

        var appConfigService = new Mock<IAppConfigService>();
        appConfigService.Setup(s => s.GetConfigForExport(null, database))
            .Returns(new AppConfig());

        var appFilter = new Mock<IAppFilter>();
        appFilter
            .Setup(s => s.FilterForMainConfig(database))
            .Returns(database);

        var sessionProvider = new Mock<ISessionProvider>();
        sessionProvider.Setup(s => s.GetSession()).Returns(session);

        var log = new Mock<ILoggingService>();

        var controller = new ConfigExportController(
            appConfigService.Object,
            appFilter.Object,
            sessionProvider.Object,
            fileContentService.Object,
            log.Object);

        var result = controller.Export(null, exportPath);

        Assert.False(result.Succeeded);
        Assert.Null(result.ExportedFileName);
        Assert.Equal("disk full", result.ErrorMessage);
    }
}
