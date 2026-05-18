using System.Text.Json;
using System.Text.RegularExpressions;
using Moq;
using RunFence.Apps;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Persistence;
using RunFence.Persistence.UI;
using Xunit;

namespace RunFence.Tests;

/// <summary>
/// Tests JSON serialization behavior for AppContainer-related models.
/// </summary>
public class AppContainerSerializationTests
{
    private static readonly JsonSerializerOptions Options = JsonDefaults.Options;

    [Fact]
    public void AppContainerEntry_Roundtrip_AllFieldsPreserved()
    {
        var entry = new AppContainerEntry
        {
            Name = "ram_browser",
            DisplayName = "Browser",
            Capabilities = ["S-1-15-3-1", "S-1-15-3-2"],
            EnableLoopback = true,
            IsEphemeral = true,
            DeleteAfterUtc = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
            LifecycleState = "CleanupPending"
        };

        var json = JsonSerializer.Serialize(entry, Options);
        var result = JsonSerializer.Deserialize<AppContainerEntry>(json, Options);

        Assert.NotNull(result);
        Assert.Equal(entry.Name, result.Name);
        Assert.Equal(entry.DisplayName, result.DisplayName);
        Assert.Equal(entry.Capabilities, result.Capabilities);
        Assert.Equal(entry.EnableLoopback, result.EnableLoopback);
        Assert.Equal(entry.IsEphemeral, result.IsEphemeral);
        Assert.Equal(entry.DeleteAfterUtc, result.DeleteAfterUtc);
        Assert.Equal(entry.LifecycleState, result.LifecycleState);
    }

    [Fact]
    public void AppContainerEntry_NullCapabilities_OmittedFromJson()
    {
        var entry = new AppContainerEntry { Name = "ram_test", DisplayName = "Test" };
        // Capabilities = null

        var json = JsonSerializer.Serialize(entry, Options);

        // With CamelCase policy and WhenWritingNull, null Capabilities must be omitted entirely
        Assert.DoesNotContain("\"capabilities\"", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AppContainerEntry_NullDeleteAfterUtc_OmittedFromJson()
    {
        var entry = new AppContainerEntry { Name = "ram_test", DisplayName = "Test" };
        // DeleteAfterUtc = null

        var json = JsonSerializer.Serialize(entry, Options);

        Assert.DoesNotContain("deleteAfterUtc", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AppEntry_MissingAppContainerName_DeserializesAsNull()
    {
        // Old JSON without the appContainerName field
        var json = """{"id":"t0001","name":"Test","exePath":"C:\\test.exe"}""";

        var result = JsonSerializer.Deserialize<AppEntry>(json, Options);

        Assert.NotNull(result);
        Assert.Null(result.AppContainerName);
    }

    [Fact]
    public void AppEntry_NullAppContainerName_OmittedFromJson()
    {
        var entry = new AppEntry { Name = "Test", ExePath = @"C:\test.exe" };
        // AppContainerName = null

        var json = JsonSerializer.Serialize(entry, Options);

        Assert.DoesNotContain("appContainerName", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AppEntry_NonNullAppContainerName_IncludedInJson()
    {
        var entry = new AppEntry
        {
            Name = "Test",
            ExePath = @"C:\test.exe",
            AppContainerName = "ram_browser"
        };

        var json = JsonSerializer.Serialize(entry, Options);

        Assert.Contains("ram_browser", json);
        Assert.Contains("appContainerName", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AppEntry_AppContainerName_Roundtrip_PreservesValue()
    {
        var entry = new AppEntry
        {
            Name = "SandboxedApp",
            ExePath = @"C:\apps\browser.exe",
            AppContainerName = "ram_browser",
            AccountSid = ""
        };

        var json = JsonSerializer.Serialize(entry, Options);
        var result = JsonSerializer.Deserialize<AppEntry>(json, Options);

        Assert.NotNull(result);
        Assert.Equal("ram_browser", result.AppContainerName);
        Assert.Equal("", result.AccountSid);
    }

    [Fact]
    public void LoadAdditionalConfig_ContainerApp_DoesNotNormalizeAccountSid()
    {
        // Container apps (AppContainerName set, AccountSid empty) must NOT have AccountSid
        // set to the current user's SID — the AppContainerName == null guard in LoadAdditionalConfig
        // must skip these entries.
        var mockDb = new Mock<IDatabaseService>();
        using var session = new SessionContext
{
            Database = new AppDatabase(),
            CredentialStore = new CredentialStore
            {
                ArgonSalt = new byte[32],
                EncryptedCanary = [1]
            },
        }.WithOwnedPinDerivedKey(TestSecretFactory.Create(32));
        var sessionProvider = new Mock<ISessionProvider>();
        sessionProvider.Setup(provider => provider.GetSession()).Returns(session);
        var handlerMappings = new Mock<IHandlerMappingService>().Object;
        var appIdValidator = new AppIdValidator();
        var configSaveOrchestrator = new ConfigSaveOrchestrator(
            sessionProvider.Object,
            () => new InlineUiThreadInvoker(action => action()),
            mockDb.Object,
            new Mock<IAppConfigService>().Object,
            handlerMappings);
        var ownershipProjection = new GrantIntentOwnershipProjectionService();
        var mainStore = new MainGrantIntentStore(
            sessionProvider.Object,
            configSaveOrchestrator,
            ownershipProjection);
        var grantIntentStoreProvider = new GrantIntentStoreProvider(
            mainStore,
            configSaveOrchestrator,
            ownershipProjection);
        var grantIntentRepository = new GrantIntentRepository(grantIntentStoreProvider);
        var appConfigService = new AppConfigService(
            new Mock<ILoggingService>().Object,
            new AppConfigIndex(ownershipProjection, appIdValidator),
            ownershipProjection,
            () => grantIntentStoreProvider,
            handlerMappings,
            mockDb.Object,
            new AppConfigSaveHelper(() => grantIntentStoreProvider, handlerMappings, mockDb.Object),
            new AppEntryIdGenerator(),
            appIdValidator);
        var database = session.Database;
        var containerApp = new AppEntry
        {
            Name = "SandboxedApp",
            ExePath = @"C:\apps\browser.exe",
            AppContainerName = "ram_browser",
            AccountSid = ""
        };

        mockDb
            .Setup(d => d.LoadAppConfigFromPath(It.IsAny<string>(), It.IsAny<ISecureSecretSnapshotSource>()))
            .Returns(new AppConfig { Apps = [containerApp] });

        // Use a temp file path outside %LOCALAPPDATA%\RunFence\ to pass the path validation guard
        var configPath = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.ramc");

        using var pinKey = TestSecretFactory.Create(32);
        appConfigService.LoadAdditionalConfig(configPath, database, pinKey);

        // AccountSid must remain empty — container apps intentionally have empty AccountSid
        Assert.Equal("", containerApp.AccountSid);
    }

    [Fact]
    public void AppContainerEntry_NullComAccessClsids_OmittedFromJson()
    {
        var entry = new AppContainerEntry { Name = "ram_test", DisplayName = "Test" };
        // ComAccessClsids = null

        var json = JsonSerializer.Serialize(entry, Options);

        Assert.DoesNotContain("\"comAccessClsids\"", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AppContainerEntry_ComAccessClsids_Roundtrip_PreservesValues()
    {
        var entry = new AppContainerEntry
        {
            Name = "ram_test",
            DisplayName = "Test",
            ComAccessClsids =
            [
                "{13709620-C279-11CE-A49E-444553540000}",
                "{F935DC22-1CF0-11D0-ADB9-00C04FD58A0B}"
            ]
        };

        var json = JsonSerializer.Serialize(entry, Options);
        var result = JsonSerializer.Deserialize<AppContainerEntry>(json, Options);

        Assert.NotNull(result);
        Assert.NotNull(result.ComAccessClsids);
        Assert.Equal(2, result.ComAccessClsids!.Count);
        Assert.Contains("{13709620-C279-11CE-A49E-444553540000}", result.ComAccessClsids);
        Assert.Contains("{F935DC22-1CF0-11D0-ADB9-00C04FD58A0B}", result.ComAccessClsids);
    }

    [Fact]
    public void AppEntry_NullPrivilegeLevel_OmittedFromJson()
    {
        var entry = new AppEntry { Name = "Test", ExePath = @"C:\test.exe" };
        // PrivilegeLevel = null (default)

        var json = JsonSerializer.Serialize(entry, Options);

        Assert.DoesNotContain("privilegeLevel", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AppEntry_MissingPrivilegeLevel_DeserializesAsNull()
    {
        var json = """{"id":"t0001","name":"Test","exePath":"C:\\test.exe"}""";

        var result = JsonSerializer.Deserialize<AppEntry>(json, Options);

        Assert.NotNull(result);
        Assert.Null(result.PrivilegeLevel);
    }

    [Fact]
    public void AppDatabase_MissingAppContainersField_DeserializesAsEmptyList()
    {
        // Simulate a config from before AppContainers was added
        var json = JsonSerializer.Serialize(new AppDatabase(), Options);
        // Remove the appContainers field to simulate old config
        json = Regex.Replace(
            json, @",?\s*""appContainers""\s*:\s*\[\s*\]", "");

        var result = JsonSerializer.Deserialize<AppDatabase>(json, Options);

        Assert.NotNull(result);
        // Property has default initializer so missing JSON field leaves it as empty list
        Assert.NotNull(result.AppContainers);
        Assert.Empty(result.AppContainers);
    }
}
