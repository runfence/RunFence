using Moq;
using RunFence.Apps;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Persistence;
using Xunit;

namespace RunFence.Tests;

/// <summary>
/// Integration tests for <see cref="AppConfigService"/> and related config management types.
/// Real file I/O (temp files via <see cref="TempDirectory"/>) is intentional — the service
/// semantics are file-path-centric (load/unload/save by path) and cannot be fully tested
/// without exercising the actual filesystem path-tracking behavior.
/// </summary>
public class AppConfigServiceTests : IDisposable
{
    private readonly AppConfigService _service;
    private readonly AppConfigIndex _index;
    private readonly GrantConfigTracker _grantTracker;
    private readonly HandlerMappingService _handlerMappings;
    private readonly Mock<IDatabaseService> _dbService;
    private readonly Mock<ILoggingService> _log;
    private readonly TempDirectory _tempDir;
    private readonly byte[] _pinKey;
    private readonly byte[] _argonSalt;

    public AppConfigServiceTests()
    {
        _log = new Mock<ILoggingService>();
        _dbService = new Mock<IDatabaseService>();
        _tempDir = new TempDirectory("RunFence_AppConfigTest");
        _grantTracker = new GrantConfigTracker();
        _index = new AppConfigIndex(_grantTracker);
        _handlerMappings = new HandlerMappingService(_index);
        _service = new AppConfigService(_log.Object, _index, _grantTracker, _handlerMappings, _dbService.Object,
            new AppConfigSaveHelper(_grantTracker, _handlerMappings, _dbService.Object),
            new AppEntryIdGenerator());
        _pinKey = new byte[32];
        new Random(42).NextBytes(_pinKey);
        _argonSalt = new byte[32];
        new Random(55).NextBytes(_argonSalt);
    }

    public void Dispose() => _tempDir.Dispose();

    private string CreateConfigFile(List<AppEntry>? apps = null)
    {
        var path = Path.Combine(_tempDir.Path, $"{Guid.NewGuid():N}.ramc");
        var config = new AppConfig { Apps = apps ?? [] };
        _dbService.Setup(d => d.LoadAppConfig(path, _pinKey)).Returns(config);
        // Create a stub file so File.Exists checks pass
        File.WriteAllText(path, "stub");
        return path;
    }

    // --- LoadAdditionalConfig ---

    [Fact]
    public void LoadAdditionalConfig_AddsAppsToDatabase()
    {
        var app = new AppEntry { Id = AppEntry.GenerateId(), Name = "TestApp" };
        var path = CreateConfigFile([app]);
        var database = new AppDatabase();

        var loaded = _service.LoadAdditionalConfig(path, database, _pinKey);

        Assert.Single(loaded);
        Assert.Single(database.Apps);
        Assert.Equal(app.Id, database.Apps[0].Id);
        Assert.Equal(_service.GetConfigPath(app.Id), Path.GetFullPath(path));
    }

    [Fact]
    public void LoadAdditionalConfig_IdCollision_RegeneratesId()
    {
        var conflictId = "AAAAA";
        var existing = new AppEntry { Id = conflictId, Name = "Existing" };
        var incoming = new AppEntry { Id = conflictId, Name = "Incoming" };
        var path = CreateConfigFile([incoming]);
        var database = new AppDatabase { Apps = [existing] };

        var loaded = _service.LoadAdditionalConfig(path, database, _pinKey);

        Assert.Single(loaded);
        Assert.Equal(2, database.Apps.Count);
        Assert.NotEqual(conflictId, loaded[0].Id);
        Assert.Contains(database.Apps, a => a.Id == conflictId); // existing unchanged
        _log.Verify(l => l.Info(It.Is<string>(s => s.Contains("ID collision"))), Times.Once);
    }

    [Fact]
    public void LoadAdditionalConfig_TracksLoadedPath()
    {
        var path = CreateConfigFile();
        var database = new AppDatabase();

        _service.LoadAdditionalConfig(path, database, _pinKey);

        Assert.True(_service.HasLoadedConfigs);
        Assert.Contains(Path.GetFullPath(path), _service.GetLoadedConfigPaths());
    }

    [Fact]
    public void LoadAdditionalConfig_DuplicatePath_IgnoresDuplicateLoad()
    {
        var app = new AppEntry { Id = AppEntry.GenerateId(), Name = "App" };
        var path = CreateConfigFile([app]);
        var database = new AppDatabase();

        _service.LoadAdditionalConfig(path, database, _pinKey);
        var second = _service.LoadAdditionalConfig(path, database, _pinKey);

        Assert.Empty(second);
        Assert.Single(database.Apps); // not duplicated
        Assert.Single(_service.GetLoadedConfigPaths()); // path listed once
        _log.Verify(l => l.Warn(It.Is<string>(s => s.Contains("already loaded"))), Times.Once);
    }

    [Fact]
    public void LoadAdditionalConfig_RejectsAppDataPaths()
    {
        var roamingPath = Path.Combine(Constants.RoamingAppDataDir, "test.ramc");

        Assert.Throws<ArgumentException>(() =>
            _service.LoadAdditionalConfig(roamingPath, new AppDatabase(), _pinKey));
    }

    [Fact]
    public void LoadAdditionalConfig_RejectsLocalAppDataPaths()
    {
        var localPath = Path.Combine(Constants.LocalAppDataDir, "test.ramc");

        Assert.Throws<ArgumentException>(() =>
            _service.LoadAdditionalConfig(localPath, new AppDatabase(), _pinKey));
    }

    // --- UnloadConfig ---

    [Fact]
    public void UnloadConfig_RemovesAppsFromDatabase()
    {
        var app = new AppEntry { Id = AppEntry.GenerateId(), Name = "App" };
        var path = CreateConfigFile([app]);
        var database = new AppDatabase();
        _service.LoadAdditionalConfig(path, database, _pinKey);

        var removed = _service.UnloadConfig(path, database);

        Assert.Single(removed);
        Assert.Empty(database.Apps);
        Assert.False(_service.HasLoadedConfigs);
        Assert.Null(_service.GetConfigPath(app.Id));
    }

    [Fact]
    public void UnloadConfig_NotLoaded_ReturnsEmpty()
    {
        var removed = _service.UnloadConfig(
            Path.Combine(_tempDir.Path, "nonexistent.ramc"),
            new AppDatabase());

        Assert.Empty(removed);
    }

    [Fact]
    public void UnloadConfig_CaseInsensitivePath()
    {
        var app = new AppEntry { Id = AppEntry.GenerateId(), Name = "App" };
        var path = CreateConfigFile([app]);
        var database = new AppDatabase();
        _service.LoadAdditionalConfig(path, database, _pinKey);

        // Unload with different casing
        var removed = _service.UnloadConfig(path.ToUpperInvariant(), database);

        Assert.Single(removed);
        Assert.Empty(database.Apps);
    }

    // --- FilterForMainConfig ---

    [Fact]
    public void FilterForMainConfig_ExcludesAdditionalConfigApps()
    {
        var mainApp = new AppEntry { Id = AppEntry.GenerateId(), Name = "MainApp" };
        var addlApp = new AppEntry { Id = AppEntry.GenerateId(), Name = "AddlApp" };
        var path = CreateConfigFile([addlApp]);
        var database = new AppDatabase { Apps = [mainApp] };
        _service.LoadAdditionalConfig(path, database, _pinKey);

        var filtered = _index.FilterForMainConfig(database);

        Assert.Single(filtered.Apps);
        Assert.Equal(mainApp.Id, filtered.Apps[0].Id);
    }

    [Fact]
    public void FilterForMainConfig_IncludesAllAppsWhenNoneLoaded()
    {
        var app1 = new AppEntry { Id = AppEntry.GenerateId(), Name = "App1" };
        var app2 = new AppEntry { Id = AppEntry.GenerateId(), Name = "App2" };
        var database = new AppDatabase { Apps = [app1, app2] };

        var filtered = _index.FilterForMainConfig(database);

        Assert.Equal(2, filtered.Apps.Count);
    }

    [Fact]
    public void FilterForMainConfig_PreservesAllNonAppsFields()
    {
        // Arrange: database with all non-Apps fields populated using V2 AccountEntry
        var container = new AppContainerEntry { Name = "TestContainer", DisplayName = "Test" };
        var grantEntry = new GrantedPathEntry { Path = @"C:\foo" };
        var groupSnapshot = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["S-1-5-21-1"] = ["grp1"]
        };
        var database = new AppDatabase
        {
            Settings = new AppSettings { EnableLogging = true },
            LastPrefsFilePath = @"C:\settings.json",
            AppContainers = [container],
            AccountGroupSnapshots = groupSnapshot,
            SidNames =
            {
                ["S-1-5-21-5"] = "TestUser"
            }
        };
        database.GetOrCreateAccount("S-1-5-21-1").IsIpcCaller = true;
        database.GetOrCreateAccount("S-1-5-21-2").TrayFolderBrowser = true;
        database.GetOrCreateAccount("S-1-5-21-3").TrayDiscovery = true;
        database.GetOrCreateAccount("S-1-5-21-4").DeleteAfterUtc = DateTime.UtcNow.AddDays(1);
        database.GetOrCreateAccount("S-1-5-21-7").PrivilegeLevel = PrivilegeLevel.LowIntegrity;
        database.GetOrCreateAccount("S-1-5-21-9").Grants.Add(grantEntry);

        var filtered = _index.FilterForMainConfig(database);

        Assert.Same(database.Settings, filtered.Settings);
        Assert.Equal(database.LastPrefsFilePath, filtered.LastPrefsFilePath);
        Assert.Same(database.SidNames, filtered.SidNames);
        Assert.Same(database.AppContainers, filtered.AppContainers);
        Assert.Same(database.AccountGroupSnapshots, filtered.AccountGroupSnapshots);
        // All accounts present (no additional config grants to filter out)
        Assert.Equal(database.Accounts.Count, filtered.Accounts.Count);
        // Non-grants flags are preserved
        Assert.True(filtered.GetAccount("S-1-5-21-1")?.IsIpcCaller);
        Assert.True(filtered.GetAccount("S-1-5-21-2")?.TrayFolderBrowser);
        Assert.True(filtered.GetAccount("S-1-5-21-3")?.TrayDiscovery);
        Assert.True(filtered.GetAccount("S-1-5-21-4")?.DeleteAfterUtc.HasValue);
        Assert.Equal(PrivilegeLevel.LowIntegrity, filtered.GetAccount("S-1-5-21-7")?.PrivilegeLevel);
        // Grant entry preserved (belongs to main config)
        Assert.Single(filtered.GetAccount("S-1-5-21-9")!.Grants);
        Assert.Equal(@"C:\foo", filtered.GetAccount("S-1-5-21-9")!.Grants[0].Path);
    }

    // --- Grant tracking ---

    [Fact]
    public void LoadAdditionalConfig_MergesGrantsIntoDatabase()
    {
        var sid = "S-1-5-21-1-1-1-1001";
        var grantEntry = new GrantedPathEntry { Path = @"C:\foo" };
        var config = new AppConfig
        {
            Accounts = [new AppConfigAccountEntry { Sid = sid, Grants = [grantEntry] }]
        };
        var path = Path.Combine(_tempDir.Path, $"{Guid.NewGuid():N}.ramc");
        _dbService.Setup(d => d.LoadAppConfig(path, _pinKey)).Returns(config);
        File.WriteAllText(path, "stub");
        var database = new AppDatabase();

        _service.LoadAdditionalConfig(path, database, _pinKey);

        var grants = database.GetAccount(sid)?.Grants;
        Assert.NotNull(grants);
        Assert.Single(grants);
        Assert.Equal(@"C:\foo", grants[0].Path);
        // The grant should be tracked as belonging to the additional config
        Assert.Equal(Path.GetFullPath(path), _grantTracker.GetGrantConfigPath(sid, grantEntry));
    }

    [Fact]
    public void UnloadConfig_RemovesGrantsFromDatabase()
    {
        var sid = "S-1-5-21-1-1-1-1001";
        var grantEntry = new GrantedPathEntry { Path = @"C:\foo" };
        var config = new AppConfig
        {
            Accounts = [new AppConfigAccountEntry { Sid = sid, Grants = [grantEntry] }]
        };
        var path = Path.Combine(_tempDir.Path, $"{Guid.NewGuid():N}.ramc");
        _dbService.Setup(d => d.LoadAppConfig(path, _pinKey)).Returns(config);
        File.WriteAllText(path, "stub");
        var database = new AppDatabase();
        _service.LoadAdditionalConfig(path, database, _pinKey);

        _service.UnloadConfig(path, database);

        // Account entry removed (it became empty after grant was removed)
        Assert.Null(database.GetAccount(sid));
        Assert.Null(_grantTracker.GetGrantConfigPath(sid, grantEntry));
    }

    [Fact]
    public void FilterForMainConfig_ExcludesAdditionalConfigGrants()
    {
        var sid = "S-1-5-21-1-1-1-1001";
        var additionalGrant = new GrantedPathEntry { Path = @"C:\additional" };
        var mainGrant = new GrantedPathEntry { Path = @"C:\main" };
        var config = new AppConfig
        {
            Accounts = [new AppConfigAccountEntry { Sid = sid, Grants = [additionalGrant] }]
        };
        var path = Path.Combine(_tempDir.Path, $"{Guid.NewGuid():N}.ramc");
        _dbService.Setup(d => d.LoadAppConfig(path, _pinKey)).Returns(config);
        File.WriteAllText(path, "stub");
        var database = new AppDatabase();
        database.GetOrCreateAccount(sid).Grants.Add(mainGrant);
        _service.LoadAdditionalConfig(path, database, _pinKey);

        var filtered = _index.FilterForMainConfig(database);

        var grants = filtered.GetAccount(sid)?.Grants;
        Assert.NotNull(grants);
        // Only the main-config grant should survive filtering
        Assert.Single(grants);
        Assert.Equal(@"C:\main", grants[0].Path);
    }

    [Fact]
    public void AssignGrant_RemoveGrant_Tracking()
    {
        var sid = "S-1-5-21-1-1-1-1001";
        var entry = new GrantedPathEntry { Path = @"C:\foo" };
        var configPath = Path.Combine(_tempDir.Path, $"{Guid.NewGuid():N}.ramc");
        File.WriteAllText(configPath, "stub");

        _grantTracker.AssignGrant(sid, entry, configPath);
        Assert.Equal(Path.GetFullPath(configPath), _grantTracker.GetGrantConfigPath(sid, entry));

        _grantTracker.RemoveGrant(sid, entry);
        Assert.Null(_grantTracker.GetGrantConfigPath(sid, entry));
    }

    // --- SaveConfigForApp ---

    [Fact]
    public void SaveConfigForApp_AdditionalConfig_CallsSaveAppConfig()
    {
        var app = new AppEntry { Id = AppEntry.GenerateId(), Name = "App" };
        var path = CreateConfigFile([app]);
        var database = new AppDatabase();
        _service.LoadAdditionalConfig(path, database, _pinKey);

        _service.SaveConfigForApp(app.Id, database, _pinKey, _argonSalt);

        _dbService.Verify(d => d.SaveAppConfig(
            It.Is<AppConfig>(c => c.Apps.Any(a => a.Id == app.Id)),
            Path.GetFullPath(path),
            _pinKey, _argonSalt), Times.Once);
    }

    [Fact]
    public void SaveConfigForApp_AdditionalConfig_IncludesHandlerMappingsInSavedConfig()
    {
        var appId = AppEntry.GenerateId();
        var app = new AppEntry { Id = appId, Name = "App" };
        var path = CreateConfigFileWithMappings([app], new Dictionary<string, HandlerMappingEntry> { [".pdf"] = new HandlerMappingEntry(appId) });
        var database = new AppDatabase();
        _service.LoadAdditionalConfig(path, database, _pinKey);

        _service.SaveConfigForApp(app.Id, database, _pinKey, _argonSalt);

        _dbService.Verify(d => d.SaveAppConfig(
            It.Is<AppConfig>(c =>
                c.HandlerMappings != null &&
                c.HandlerMappings.ContainsKey(".pdf") &&
                c.HandlerMappings[".pdf"].AppId == appId),
            Path.GetFullPath(path),
            _pinKey, _argonSalt), Times.Once);
    }

    [Fact]
    public void SaveConfigForApp_MainConfig_CallsSaveConfig()
    {
        var app = new AppEntry { Id = AppEntry.GenerateId(), Name = "MainApp" };
        var database = new AppDatabase { Apps = [app] };

        _service.SaveConfigForApp(app.Id, database, _pinKey, _argonSalt);

        _dbService.Verify(d => d.SaveConfig(database, _pinKey, _argonSalt), Times.Once);
        _dbService.Verify(d => d.SaveAppConfig(
                It.IsAny<AppConfig>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<byte[]>()),
            Times.Never);
    }

    [Fact]
    public void SaveAllConfigs_CallsSaveConfigAndSaveAppConfigForEachLoaded()
    {
        var app1 = new AppEntry { Id = AppEntry.GenerateId(), Name = "App1" };
        var app2 = new AppEntry { Id = AppEntry.GenerateId(), Name = "App2" };
        var path1 = CreateConfigFile([app1]);
        var path2 = CreateConfigFile([app2]);
        var database = new AppDatabase();
        _service.LoadAdditionalConfig(path1, database, _pinKey);
        _service.LoadAdditionalConfig(path2, database, _pinKey);

        _service.SaveAllConfigs(database, _pinKey, _argonSalt);

        _dbService.Verify(d => d.SaveConfig(database, _pinKey, _argonSalt), Times.Once);
        _dbService.Verify(d => d.SaveAppConfig(
            It.IsAny<AppConfig>(), Path.GetFullPath(path1), _pinKey, _argonSalt), Times.Once);
        _dbService.Verify(d => d.SaveAppConfig(
            It.IsAny<AppConfig>(), Path.GetFullPath(path2), _pinKey, _argonSalt), Times.Once);
    }

    [Fact]
    public void CreateEmptyConfig_CallsSaveAppConfigWithEmptyApps()
    {
        var path = Path.Combine(_tempDir.Path, $"{Guid.NewGuid():N}.ramc");

        _service.CreateEmptyConfig(path, _pinKey, _argonSalt);

        _dbService.Verify(d => d.SaveAppConfig(
            It.Is<AppConfig>(c => !c.Apps.Any()),
            Path.GetFullPath(path),
            _pinKey, _argonSalt), Times.Once);
    }

    [Fact]
    public void SaveImportedConfig_CallsSaveAppConfigWithProvidedApps()
    {
        var app = new AppEntry { Id = AppEntry.GenerateId(), Name = "Imported" };
        var path = Path.Combine(_tempDir.Path, $"{Guid.NewGuid():N}.ramc");

        _service.SaveImportedConfig(path, [app], _pinKey, _argonSalt);

        _dbService.Verify(d => d.SaveAppConfig(
            It.Is<AppConfig>(c => c.Apps.Any(a => a.Id == app.Id)),
            Path.GetFullPath(path),
            _pinKey, _argonSalt), Times.Once);
    }

    // --- GetLoadedConfigPaths (ordered) ---

    // --- ReencryptAndSaveAll ---

    [Fact]
    public void ReencryptAndSaveAll_CallsSaveCredentialStoreAndAllConfigsWithNewKey()
    {
        // Arrange: load two additional configs so ReencryptAndSaveAll has something to re-encrypt
        var mainApp = new AppEntry { Id = AppEntry.GenerateId(), Name = "MainApp" };
        var extraApp1 = new AppEntry { Id = AppEntry.GenerateId(), Name = "ExtraApp1" };
        var extraApp2 = new AppEntry { Id = AppEntry.GenerateId(), Name = "ExtraApp2" };
        var path1 = CreateConfigFile([extraApp1]);
        var path2 = CreateConfigFile([extraApp2]);
        var database = new AppDatabase { Apps = [mainApp] };
        _service.LoadAdditionalConfig(path1, database, _pinKey);
        _service.LoadAdditionalConfig(path2, database, _pinKey);

        var newKey = new byte[32];
        new Random(88).NextBytes(newKey);
        var store = new CredentialStore
        {
            ArgonSalt = _argonSalt,
            EncryptedCanary = [1, 2, 3]
        };

        List<(string, AppConfig)>? capturedConfigs = null;
        byte[]? capturedKey = null;
        _dbService
            .Setup(d => d.SaveCredentialStoreAndAllConfigs(
                It.IsAny<CredentialStore>(),
                It.IsAny<AppDatabase>(),
                It.IsAny<byte[]>(),
                It.IsAny<List<(string, AppConfig)>>()))
            .Callback<CredentialStore, AppDatabase, byte[], List<(string, AppConfig)>>(
                (_, _, key, configs) =>
                {
                    capturedKey = key;
                    capturedConfigs = configs;
                });

        // Act
        _service.ReencryptAndSaveAll(store, database, newKey);

        // Assert: called once with the new key
        _dbService.Verify(d => d.SaveCredentialStoreAndAllConfigs(
            store,
            database,
            newKey,
            It.IsAny<List<(string, AppConfig)>>()), Times.Once);

        Assert.NotNull(capturedKey);
        Assert.Equal(newKey, capturedKey);

        // Both additional configs must be included
        Assert.NotNull(capturedConfigs);
        Assert.Equal(2, capturedConfigs!.Count);
        Assert.Contains(capturedConfigs, c => c.Item1 == Path.GetFullPath(path1));
        Assert.Contains(capturedConfigs, c => c.Item1 == Path.GetFullPath(path2));
    }

    [Fact]
    public void ReencryptAndSaveAll_NoAdditionalConfigs_CallsSaveWithEmptyList()
    {
        // Arrange: no additional configs loaded — only the main config is re-encrypted
        var store = new CredentialStore
        {
            ArgonSalt = _argonSalt,
            EncryptedCanary = [1, 2, 3]
        };
        var database = new AppDatabase();
        var newKey = new byte[32];
        new Random(77).NextBytes(newKey);

        List<(string, AppConfig)>? capturedConfigs = null;
        _dbService
            .Setup(d => d.SaveCredentialStoreAndAllConfigs(
                It.IsAny<CredentialStore>(),
                It.IsAny<AppDatabase>(),
                It.IsAny<byte[]>(),
                It.IsAny<List<(string, AppConfig)>>()))
            .Callback<CredentialStore, AppDatabase, byte[], List<(string, AppConfig)>>(
                (_, _, _, configs) => capturedConfigs = configs);

        // Act
        _service.ReencryptAndSaveAll(store, database, newKey);

        // Assert: called once; additional configs list is empty
        _dbService.Verify(d => d.SaveCredentialStoreAndAllConfigs(
            store, database, newKey, It.IsAny<List<(string, AppConfig)>>()), Times.Once);
        Assert.NotNull(capturedConfigs);
        Assert.Empty(capturedConfigs!);
    }

    [Fact]
    public void GetLoadedConfigPaths_ReturnsInsertionOrder()
    {
        var path1 = CreateConfigFile();
        var path2 = CreateConfigFile();
        var path3 = CreateConfigFile();
        var database = new AppDatabase();

        _service.LoadAdditionalConfig(path1, database, _pinKey);
        _service.LoadAdditionalConfig(path2, database, _pinKey);
        _service.LoadAdditionalConfig(path3, database, _pinKey);

        var paths = _service.GetLoadedConfigPaths();
        Assert.Equal(3, paths.Count);
        Assert.Equal(Path.GetFullPath(path1), paths[0]);
        Assert.Equal(Path.GetFullPath(path2), paths[1]);
        Assert.Equal(Path.GetFullPath(path3), paths[2]);
    }

    // --- Handler Mapping helpers ---

    private string CreateConfigFileWithMappings(List<AppEntry>? apps, Dictionary<string, HandlerMappingEntry>? handlerMappings)
    {
        var path = Path.Combine(_tempDir.Path, $"{Guid.NewGuid():N}.ramc");
        var config = new AppConfig { Apps = apps ?? [], HandlerMappings = handlerMappings };
        _dbService.Setup(d => d.LoadAppConfig(path, _pinKey)).Returns(config);
        File.WriteAllText(path, "stub");
        return path;
    }

    // --- GetEffectiveHandlerMappings ---

    [Fact]
    public void GetEffectiveHandlerMappings_MergesMainAndExtraConfigs()
    {
        var mainApp = new AppEntry { Id = AppEntry.GenerateId(), Name = "MainApp" };
        var extraApp = new AppEntry { Id = AppEntry.GenerateId(), Name = "ExtraApp" };
        var database = new AppDatabase
        {
            Apps = [mainApp],
            Settings = { HandlerMappings = new Dictionary<string, HandlerMappingEntry> { [".pdf"] = new HandlerMappingEntry(mainApp.Id) } }
        };
        var extraPath = CreateConfigFileWithMappings([extraApp],
            new Dictionary<string, HandlerMappingEntry> { ["http"] = new HandlerMappingEntry(extraApp.Id) });
        _service.LoadAdditionalConfig(extraPath, database, _pinKey);

        var effective = _handlerMappings.GetEffectiveHandlerMappings(database);

        Assert.Equal(2, effective.Count);
        Assert.Equal(mainApp.Id, effective[".pdf"].AppId);
        Assert.Equal(extraApp.Id, effective["http"].AppId);
    }

    [Fact]
    public void GetEffectiveHandlerMappings_ExtraConfigOverridesMainOnDuplicateKey()
    {
        var mainApp = new AppEntry { Id = AppEntry.GenerateId(), Name = "MainApp" };
        var extraApp = new AppEntry { Id = AppEntry.GenerateId(), Name = "ExtraApp" };
        var database = new AppDatabase
        {
            Apps = [mainApp],
            Settings = { HandlerMappings = new Dictionary<string, HandlerMappingEntry> { ["http"] = new HandlerMappingEntry(mainApp.Id) } }
        };
        var extraPath = CreateConfigFileWithMappings([extraApp],
            new Dictionary<string, HandlerMappingEntry> { ["http"] = new HandlerMappingEntry(extraApp.Id) });
        _service.LoadAdditionalConfig(extraPath, database, _pinKey);

        var effective = _handlerMappings.GetEffectiveHandlerMappings(database);

        // Extra config wins on duplicate key
        Assert.Single(effective);
        Assert.Equal(extraApp.Id, effective["http"].AppId);
    }

    // --- GetAllHandlerMappings ---

    [Fact]
    public void GetAllHandlerMappings_ReturnsBothWhenSameKeyInMultipleConfigs()
    {
        var mainApp = new AppEntry { Id = AppEntry.GenerateId(), Name = "MainApp" };
        var extraApp = new AppEntry { Id = AppEntry.GenerateId(), Name = "ExtraApp" };
        var database = new AppDatabase
        {
            Apps = [mainApp],
            Settings = { HandlerMappings = new Dictionary<string, HandlerMappingEntry> { ["http"] = new HandlerMappingEntry(mainApp.Id) } }
        };
        var extraPath = CreateConfigFileWithMappings([extraApp],
            new Dictionary<string, HandlerMappingEntry> { ["http"] = new HandlerMappingEntry(extraApp.Id) });
        _service.LoadAdditionalConfig(extraPath, database, _pinKey);

        var all = _handlerMappings.GetAllHandlerMappings(database);

        // Both apps appear for "http", main config first
        Assert.Single(all);
        Assert.Equal(2, all["http"].Count);
        Assert.Equal(mainApp.Id, all["http"][0].AppId);
        Assert.Equal(extraApp.Id, all["http"][1].AppId);
    }

    [Fact]
    public void GetAllHandlerMappings_MergesDistinctKeysFromAllConfigs()
    {
        var mainApp = new AppEntry { Id = AppEntry.GenerateId(), Name = "MainApp" };
        var extraApp = new AppEntry { Id = AppEntry.GenerateId(), Name = "ExtraApp" };
        var database = new AppDatabase
        {
            Apps = [mainApp],
            Settings = { HandlerMappings = new Dictionary<string, HandlerMappingEntry> { [".pdf"] = new HandlerMappingEntry(mainApp.Id) } }
        };
        var extraPath = CreateConfigFileWithMappings([extraApp],
            new Dictionary<string, HandlerMappingEntry> { ["http"] = new HandlerMappingEntry(extraApp.Id) });
        _service.LoadAdditionalConfig(extraPath, database, _pinKey);

        var all = _handlerMappings.GetAllHandlerMappings(database);

        Assert.Equal(2, all.Count);
        Assert.Equal(mainApp.Id, Assert.Single(all[".pdf"]).AppId);
        Assert.Equal(extraApp.Id, Assert.Single(all["http"]).AppId);
    }

    // --- SetHandlerMapping ---

    [Fact]
    public void SetHandlerMapping_RoutesToMainConfigForMainApp()
    {
        var app = new AppEntry { Id = AppEntry.GenerateId(), Name = "App" };
        var database = new AppDatabase { Apps = [app] };

        _handlerMappings.SetHandlerMapping("http", new HandlerMappingEntry(app.Id), database);

        Assert.NotNull(database.Settings.HandlerMappings);
        Assert.Equal(app.Id, database.Settings.HandlerMappings["http"].AppId);
    }

    [Fact]
    public void SetHandlerMapping_RoutesToExtraConfigForExtraApp()
    {
        var app = new AppEntry { Id = AppEntry.GenerateId(), Name = "ExtraApp" };
        var database = new AppDatabase();
        var path = CreateConfigFile([app]);
        _service.LoadAdditionalConfig(path, database, _pinKey);

        _handlerMappings.SetHandlerMapping("http", new HandlerMappingEntry(app.Id), database);

        // Not in main config
        Assert.Null(database.Settings.HandlerMappings);
        // In extra config
        var mappingsForConfig = _handlerMappings.GetHandlerMappingsForConfig(path);
        Assert.NotNull(mappingsForConfig);
        Assert.Equal(app.Id, mappingsForConfig["http"].AppId);
    }

    [Fact]
    public void SetHandlerMapping_AddsToOwnConfig_PreservesOtherConfigMappings()
    {
        var mainApp = new AppEntry { Id = AppEntry.GenerateId(), Name = "MainApp" };
        var extraApp = new AppEntry { Id = AppEntry.GenerateId(), Name = "ExtraApp" };
        var database = new AppDatabase
        {
            Apps = [mainApp],
            Settings = { HandlerMappings = new Dictionary<string, HandlerMappingEntry> { ["http"] = new HandlerMappingEntry(mainApp.Id) } }
        };
        var path = CreateConfigFile([extraApp]);
        _service.LoadAdditionalConfig(path, database, _pinKey);

        // Add "http" for extra app (different config than main app)
        _handlerMappings.SetHandlerMapping("http", new HandlerMappingEntry(extraApp.Id), database);

        // Main config mapping preserved
        Assert.NotNull(database.Settings.HandlerMappings);
        Assert.Equal(mainApp.Id, database.Settings.HandlerMappings["http"].AppId);
        // Extra config also has "http"
        var mappingsForConfig = _handlerMappings.GetHandlerMappingsForConfig(path);
        Assert.NotNull(mappingsForConfig);
        Assert.Equal(extraApp.Id, mappingsForConfig["http"].AppId);
    }

    // --- RemoveHandlerMapping ---

    [Fact]
    public void RemoveHandlerMapping_RemovesFromMainConfig()
    {
        var database = new AppDatabase
        {
            Settings = { HandlerMappings = new Dictionary<string, HandlerMappingEntry> { ["http"] = new HandlerMappingEntry("app1"), [".pdf"] = new HandlerMappingEntry("app1") } }
        };

        _handlerMappings.RemoveHandlerMapping("http", "app1", database);

        // "http" removed, ".pdf" still present
        Assert.NotNull(database.Settings.HandlerMappings);
        Assert.False(database.Settings.HandlerMappings.ContainsKey("http"));
        Assert.Single(database.Settings.HandlerMappings);
        Assert.Equal("app1", database.Settings.HandlerMappings[".pdf"].AppId);
    }

    [Fact]
    public void RemoveHandlerMapping_SetsHandlerMappingsToNullWhenEmpty()
    {
        var database = new AppDatabase
        {
            Settings = { HandlerMappings = new Dictionary<string, HandlerMappingEntry> { ["http"] = new HandlerMappingEntry("app1") } }
        };

        _handlerMappings.RemoveHandlerMapping("http", "app1", database);

        Assert.Null(database.Settings.HandlerMappings);
    }

    [Fact]
    public void RemoveHandlerMapping_DoesNotRemoveWhenValueBelongsToDifferentApp_MainConfig()
    {
        // Main config has "http" → "app1"; calling remove for "app2" (also in main config) must not remove it
        var database = new AppDatabase
        {
            Settings = { HandlerMappings = new Dictionary<string, HandlerMappingEntry> { ["http"] = new HandlerMappingEntry("app1") } }
        };

        _handlerMappings.RemoveHandlerMapping("http", "app2", database);

        Assert.NotNull(database.Settings.HandlerMappings);
        Assert.Equal("app1", database.Settings.HandlerMappings["http"].AppId);
    }

    [Fact]
    public void RemoveHandlerMapping_DoesNotRemoveWhenValueBelongsToDifferentApp_ExtraConfig()
    {
        // Extra config has "http" → appA and ".pdf" → appB; removing ("http", appB) must not touch appA's mapping
        var appA = new AppEntry { Id = AppEntry.GenerateId(), Name = "AppA" };
        var appB = new AppEntry { Id = AppEntry.GenerateId(), Name = "AppB" };
        var database = new AppDatabase();
        var path = CreateConfigFileWithMappings([appA, appB],
            new Dictionary<string, HandlerMappingEntry> { ["http"] = new HandlerMappingEntry(appA.Id), [".pdf"] = new HandlerMappingEntry(appB.Id) });
        _service.LoadAdditionalConfig(path, database, _pinKey);

        _handlerMappings.RemoveHandlerMapping("http", appB.Id, database);

        var mappings = _handlerMappings.GetHandlerMappingsForConfig(path);
        Assert.NotNull(mappings);
        Assert.Equal(appA.Id, mappings["http"].AppId); // appA's mapping preserved
        Assert.Equal(appB.Id, mappings[".pdf"].AppId);  // appB's mapping preserved
    }

    [Fact]
    public void RemoveHandlerMapping_RemovesFromExtraConfig()
    {
        var app = new AppEntry { Id = AppEntry.GenerateId(), Name = "App" };
        var database = new AppDatabase();
        var path = CreateConfigFileWithMappings([app],
            new Dictionary<string, HandlerMappingEntry> { ["http"] = new HandlerMappingEntry(app.Id) });
        _service.LoadAdditionalConfig(path, database, _pinKey);

        _handlerMappings.RemoveHandlerMapping("http", app.Id, database);

        var mappings = _handlerMappings.GetHandlerMappingsForConfig(path);
        Assert.Null(mappings); // empty after removal → null
    }

    // --- Load/Unload lifecycle ---

    [Fact]
    public void LoadConfig_RestoresExtraHandlerMappings()
    {
        var app = new AppEntry { Id = AppEntry.GenerateId(), Name = "App" };
        var database = new AppDatabase();
        var path = CreateConfigFileWithMappings([app],
            new Dictionary<string, HandlerMappingEntry> { ["http"] = new HandlerMappingEntry(app.Id) });

        _service.LoadAdditionalConfig(path, database, _pinKey);

        var effective = _handlerMappings.GetEffectiveHandlerMappings(database);
        Assert.Equal(app.Id, effective["http"].AppId);
    }

    [Fact]
    public void UnloadConfig_RemovesExtraHandlerMappings()
    {
        var app = new AppEntry { Id = AppEntry.GenerateId(), Name = "App" };
        var database = new AppDatabase();
        var path = CreateConfigFileWithMappings([app],
            new Dictionary<string, HandlerMappingEntry> { ["http"] = new HandlerMappingEntry(app.Id) });
        _service.LoadAdditionalConfig(path, database, _pinKey);

        _service.UnloadConfig(path, database);

        var effective = _handlerMappings.GetEffectiveHandlerMappings(database);
        Assert.False(effective.ContainsKey("http"));
    }
}