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
/// Integration tests for <see cref="AppConfigService"/> and related config management types.
/// Real file I/O (temp files via <see cref="TempDirectory"/>) is intentional — the service
/// semantics are file-path-centric (load/unload/save by path) and cannot be fully tested
/// without exercising the actual filesystem path-tracking behavior.
/// </summary>
public class AppConfigServiceTests : IDisposable
{
    private readonly SessionContext _session;
    private readonly AppConfigService _service;
    private readonly AppConfigIndex _index;
    private readonly GrantIntentStoreProvider _grantIntentStoreProvider;
    private readonly GrantIntentRepository _grantIntentRepository;
    private readonly HandlerMappingService _handlerMappings;
    private readonly Mock<IDatabaseService> _dbService;
    private readonly Mock<ILoggingService> _log;
    private readonly TempDirectory _tempDir;
    private readonly SecureSecret _pinKey;
    private readonly byte[] _argonSalt;

    public AppConfigServiceTests()
    {
        _log = new Mock<ILoggingService>();
        _dbService = new Mock<IDatabaseService>();
        _tempDir = new TempDirectory("RunFence_AppConfigTest");
        _pinKey = TestSecretFactory.Create(32);
        _session = new SessionContext
{
            Database = new AppDatabase(),
            CredentialStore = new CredentialStore
            {
                ArgonSalt = new byte[32],
                EncryptedCanary = [1]
            },
        }.WithOwnedPinDerivedKey(_pinKey);
        var appIdValidator = new AppIdValidator();
        var sessionProvider = new Mock<ISessionProvider>();
        sessionProvider.Setup(provider => provider.GetSession()).Returns(_session);
        var configSaveOrchestrator = new ConfigSaveOrchestrator(
            sessionProvider.Object,
            () => new InlineUiThreadInvoker(action => action()),
            _dbService.Object,
            new Mock<IAppConfigService>().Object,
            new Mock<IHandlerMappingService>().Object);
        var ownershipProjection = new GrantIntentOwnershipProjectionService();
        var mainStore = new MainGrantIntentStore(
            sessionProvider.Object,
            configSaveOrchestrator,
            ownershipProjection);
        _grantIntentStoreProvider = new GrantIntentStoreProvider(
            mainStore,
            configSaveOrchestrator,
            ownershipProjection);
        _grantIntentRepository = new GrantIntentRepository(_grantIntentStoreProvider);
        _index = new AppConfigIndex(ownershipProjection, appIdValidator);
        _handlerMappings = new HandlerMappingService(_index);
        _service = new AppConfigService(
            _log.Object,
            _index,
            ownershipProjection,
            () => _grantIntentStoreProvider,
            _handlerMappings,
            _dbService.Object,
            new AppConfigSaveHelper(() => _grantIntentStoreProvider, _handlerMappings, _dbService.Object),
            new AppEntryIdGenerator(),
            appIdValidator);
        _argonSalt = new byte[32];
        new Random(55).NextBytes(_argonSalt);
    }

    public void Dispose()
    {
        _session.Dispose();
        _pinKey.Dispose();
        _tempDir.Dispose();
    }

    private void UseDatabase(AppDatabase database)
        => _session.Database = database;

    private string CreateConfigFile(List<AppEntry>? apps = null,
        Dictionary<string, HandlerMappingEntry>? handlerMappings = null,
        List<AppConfigAccountEntry>? accounts = null)
    {
        var path = Path.Combine(_tempDir.Path, $"{Guid.NewGuid():N}.ramc");
        var config = new AppConfig
        {
            Apps = apps ?? [],
            HandlerMappings = handlerMappings,
            Accounts = accounts
        };
        _dbService.Setup(d => d.LoadAppConfigFromPath(path, _pinKey)).Returns(config);
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
    public void CaptureRuntimeStateSnapshot_RestoreRuntimeStateSnapshot_RestoresMappingsLoadedPathsAndHandlerMappings()
    {
        var app = new AppEntry { Id = AppEntry.GenerateId(), Name = "TrackedApp" };
        var path = CreateConfigFile(
            [app],
            new Dictionary<string, HandlerMappingEntry>(StringComparer.OrdinalIgnoreCase)
            {
                ["http"] = new(app.Id)
            });
        var database = new AppDatabase();
        _service.LoadAdditionalConfig(path, database, _pinKey);
        var snapshot = _service.CaptureRuntimeStateSnapshot();

        _handlerMappings.RenameAppIdInConfigMappings(path, app.Id, "renamed-app");
        _service.RemoveApp(app.Id);
        _service.UnloadConfig(path, database);
        _service.RestoreRuntimeStateSnapshot(snapshot);

        Assert.Equal(Path.GetFullPath(path), _service.GetConfigPath(app.Id));
        Assert.Equal([Path.GetFullPath(path)], _service.GetLoadedConfigPaths());
        Assert.Equal(app.Id, _handlerMappings.GetHandlerMappingsForConfig(path)!["http"].AppId);
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
        var roamingPath = Path.Combine(PathConstants.RoamingAppDataDir, "test.ramc");

        Assert.Throws<ArgumentException>(() =>
            _service.LoadAdditionalConfig(roamingPath, new AppDatabase(), _pinKey));
    }

    [Fact]
    public void LoadAdditionalConfig_RejectsLocalAppDataPaths()
    {
        var localPath = Path.Combine(PathConstants.LocalAppDataDir, "test.ramc");

        Assert.Throws<ArgumentException>(() =>
            _service.LoadAdditionalConfig(localPath, new AppDatabase(), _pinKey));
    }

    [Fact]
    public void LoadAdditionalConfig_InvalidAppId_ThrowsInvalidAppIdException()
    {
        var path = CreateConfigFile([new AppEntry { Id = @"..\escape", Name = "Bad" }]);

        var ex = Assert.Throws<InvalidAppIdException>(() =>
            _service.LoadAdditionalConfig(path, new AppDatabase(), _pinKey));

        Assert.Contains("invalid", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LoadAdditionalConfig_InvalidHandlerMappingReference_ThrowsWithoutPartialMutation()
    {
        var path = CreateConfigFile(
            apps: [new AppEntry { Id = "app-1", Name = "App" }],
            handlerMappings: new Dictionary<string, HandlerMappingEntry>(StringComparer.OrdinalIgnoreCase)
            {
                ["http"] = new("missing-app")
            });
        var database = new AppDatabase();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            _service.LoadAdditionalConfig(path, database, _pinKey));

        Assert.Contains("unknown app ID", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(database.Apps);
        Assert.False(_service.HasLoadedConfigs);
    }

    [Fact]
    public void LoadAdditionalConfig_IdCollision_RenamesHandlerMappingAppId()
    {
        const string sharedId = "shared-id";
        var existing = new AppEntry { Id = sharedId, Name = "Existing" };
        var incoming = new AppEntry { Id = sharedId, Name = "Incoming" };
        var path = CreateConfigFile(
            apps: [incoming],
            handlerMappings: new Dictionary<string, HandlerMappingEntry>(StringComparer.OrdinalIgnoreCase)
            {
                ["http"] = new(sharedId)
            });
        var database = new AppDatabase { Apps = [existing] };

        var loaded = _service.LoadAdditionalConfig(path, database, _pinKey);
        var loadedApp = Assert.Single(loaded);
        var mappings = _handlerMappings.GetHandlerMappingsForConfig(path);

        Assert.NotNull(mappings);
        Assert.Equal(loadedApp.Id, mappings!["http"].AppId);
        Assert.NotEqual(sharedId, loadedApp.Id);
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
            Settings = new AppSettings { LogVerbosity = LogVerbosity.Debug },
            LastPrefsFilePath = @"C:\settings.json",
            AppContainers = [container],
            AccountGroupSnapshots = groupSnapshot,
            ShowSystemInRunAs = true,
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
        UseDatabase(database);

        var filtered = _index.FilterForMainConfig(database);

        Assert.NotSame(database.Settings, filtered.Settings); // Settings is cloned to prevent shared mutation
        Assert.Equal(database.Settings.LogVerbosity, filtered.Settings.LogVerbosity);
        Assert.Equal(database.LastPrefsFilePath, filtered.LastPrefsFilePath);
        Assert.NotSame(database.SidNames, filtered.SidNames);
        Assert.NotSame(database.AppContainers, filtered.AppContainers);
        Assert.NotSame(database.AccountGroupSnapshots, filtered.AccountGroupSnapshots);
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
        // ShowSystemInRunAs preserved
        Assert.Equal(database.ShowSystemInRunAs, filtered.ShowSystemInRunAs);
    }

    // --- GetConfigForExport ---

    [Fact]
    public void GetConfigForExport_ReturnsAppsGrantsAndHandlerMappings()
    {
        // Arrange: load an additional config with one app, one grant, and one handler mapping
        var sid = "S-1-5-21-1-1-1-2001";
        var appId = AppEntry.GenerateId();
        var app = new AppEntry { Id = appId, Name = "ExportApp" };
        var grantEntry = new GrantedPathEntry { Path = @"C:\exported" };
        var mapping = new HandlerMappingEntry(appId);
        var path = CreateConfigFile(
            apps: [app],
            handlerMappings: new Dictionary<string, HandlerMappingEntry> { [".xyz"] = mapping },
            accounts: [new AppConfigAccountEntry { Sid = sid, Grants = [grantEntry] }]);
        var database = new AppDatabase();
        _service.LoadAdditionalConfig(path, database, _pinKey);

        // Act
        var exported = _service.GetConfigForExport(path, database);

        // Assert: all three data groups are present
        Assert.Single(exported.Apps);
        Assert.Equal(appId, exported.Apps[0].Id);
        Assert.NotNull(exported.Accounts);
        Assert.Single(exported.Accounts);
        Assert.Equal(sid, exported.Accounts[0].Sid);
        Assert.Single(exported.Accounts[0].Grants);
        Assert.Equal(@"C:\exported", exported.Accounts[0].Grants[0].Path);
        Assert.NotNull(exported.HandlerMappings);
        Assert.True(exported.HandlerMappings.ContainsKey(".xyz"));
        Assert.Equal(appId, exported.HandlerMappings[".xyz"].AppId);
    }

    [Fact]
    public void GetConfigForExport_MainConfig_PreservesAllApplicationPackagesTraverseEntriesInAccounts()
    {
        var grant = new GrantedPathEntry { Path = @"C:\shared-main", IsTraverseOnly = true };
        var database = new AppDatabase();
        database.GetOrCreateAccount(WellKnownSecuritySids.AllApplicationPackagesSid).Grants.Add(grant);
        UseDatabase(database);

        var exported = _service.GetConfigForExport(null, database);

        var account = Assert.Single(exported.Accounts!);
        Assert.Equal(WellKnownSecuritySids.AllApplicationPackagesSid, account.Sid);
        var traverse = Assert.Single(account.Grants);
        Assert.Equal(@"C:\shared-main", traverse.Path);
        Assert.True(traverse.IsTraverseOnly);
    }

    [Fact]
    public void GetConfigForExport_MainConfig_AllApplicationPackagesGrant_StaysInAccounts()
    {
        var grant = new GrantedPathEntry
        {
            Path = @"C:\aap-main-grant",
            SavedRights = SavedRightsState.DefaultForMode(isDeny: false)
        };
        var database = new AppDatabase();
        database.GetOrCreateAccount(WellKnownSecuritySids.AllApplicationPackagesSid).Grants.Add(grant);
        UseDatabase(database);

        var exported = _service.GetConfigForExport(null, database);

        var account = Assert.Single(exported.Accounts!);
        Assert.Equal(WellKnownSecuritySids.AllApplicationPackagesSid, account.Sid);
        Assert.Single(account.Grants);
        Assert.Equal(@"C:\aap-main-grant", account.Grants[0].Path);
        Assert.False(account.Grants[0].IsTraverseOnly);
    }

    [Fact]
    public void GetConfigForExport_MainConfig_AllApplicationPackagesGrantAndTraverse_StayInAccounts()
    {
        var grant = new GrantedPathEntry
        {
            Path = @"C:\aap-main-grant",
            SavedRights = SavedRightsState.DefaultForMode(isDeny: false)
        };
        var traverse = new GrantedPathEntry
        {
            Path = @"C:\aap-main-traverse",
            IsTraverseOnly = true
        };
        var database = new AppDatabase();
        var account = database.GetOrCreateAccount(WellKnownSecuritySids.AllApplicationPackagesSid);
        account.Grants.Add(grant);
        account.Grants.Add(traverse);
        UseDatabase(database);

        var exported = _service.GetConfigForExport(null, database);

        var exportedAccount = Assert.Single(exported.Accounts!);
        Assert.Equal(2, exportedAccount.Grants.Count);
        Assert.Contains(exportedAccount.Grants, entry => entry.Path == @"C:\aap-main-grant" && !entry.IsTraverseOnly);
        Assert.Contains(exportedAccount.Grants, entry => entry.Path == @"C:\aap-main-traverse" && entry.IsTraverseOnly);
    }

    // --- Grant tracking ---

    [Fact]
    public void LoadAdditionalConfig_MergesGrantsIntoDatabase()
    {
        var sid = "S-1-5-21-1-1-1-1001";
        var grantEntry = new GrantedPathEntry { Path = @"C:\foo" };
        var path = CreateConfigFile(accounts: [new AppConfigAccountEntry { Sid = sid, Grants = [grantEntry] }]);
        var database = new AppDatabase();

        _service.LoadAdditionalConfig(path, database, _pinKey);

        var grants = database.GetAccount(sid)?.Grants;
        Assert.NotNull(grants);
        Assert.Single(grants);
        Assert.Equal(@"C:\foo", grants[0].Path);
        // The grant should be tracked as belonging to the additional config
        Assert.Equal(Path.GetFullPath(path), GetGrantConfigPath(sid, grantEntry));
    }

    [Fact]
    public void UnloadConfig_RemovesGrantsFromDatabase()
    {
        var sid = "S-1-5-21-1-1-1-1001";
        var grantEntry = new GrantedPathEntry { Path = @"C:\foo" };
        var path = CreateConfigFile(accounts: [new AppConfigAccountEntry { Sid = sid, Grants = [grantEntry] }]);
        var database = new AppDatabase();
        _service.LoadAdditionalConfig(path, database, _pinKey);

        _service.UnloadConfig(path, database);

        // Account entry removed (it became empty after grant was removed)
        Assert.Null(database.GetAccount(sid));
        Assert.Null(GetGrantConfigPath(sid, grantEntry));
    }

    [Fact]
    public void FilterForMainConfig_ExcludesAdditionalConfigGrants()
    {
        var sid = "S-1-5-21-1-1-1-1001";
        var additionalGrant = new GrantedPathEntry { Path = @"C:\additional" };
        var mainGrant = new GrantedPathEntry { Path = @"C:\main" };
        var path = CreateConfigFile(accounts: [new AppConfigAccountEntry { Sid = sid, Grants = [additionalGrant] }]);
        var database = new AppDatabase();
        database.GetOrCreateAccount(sid).Grants.Add(mainGrant);
        UseDatabase(database);
        _service.LoadAdditionalConfig(path, database, _pinKey);

        var filtered = _index.FilterForMainConfig(database);

        var grants = filtered.GetAccount(sid)?.Grants;
        Assert.NotNull(grants);
        // Only the main-config grant should survive filtering
        Assert.Single(grants);
        Assert.Equal(@"C:\main", grants[0].Path);
    }

    [Fact]
    public void LoadAdditionalConfig_MergesAllApplicationPackagesTraverseEntriesIntoDatabase()
    {
        var sharedEntry = new GrantedPathEntry { Path = @"C:\shared", IsTraverseOnly = true };
        var path = CreateConfigFile(accounts:
        [
            new AppConfigAccountEntry
            {
                Sid = WellKnownSecuritySids.AllApplicationPackagesSid,
                Grants = [sharedEntry]
            }
        ]);
        var database = new AppDatabase();

        _service.LoadAdditionalConfig(path, database, _pinKey);

        var account = Assert.Single(database.Accounts);
        Assert.Equal(WellKnownSecuritySids.AllApplicationPackagesSid, account.Sid);
        Assert.Equal(@"C:\shared", Assert.Single(account.Grants).Path);
        Assert.Equal(Path.GetFullPath(path), GetGrantConfigPath(
            WellKnownSecuritySids.AllApplicationPackagesSid,
            sharedEntry));
    }

    [Fact]
    public void UnloadConfig_RemovesAllApplicationPackagesTraverseEntriesFromDatabase()
    {
        var sharedEntry = new GrantedPathEntry { Path = @"C:\shared", IsTraverseOnly = true };
        var path = CreateConfigFile(accounts:
        [
            new AppConfigAccountEntry
            {
                Sid = WellKnownSecuritySids.AllApplicationPackagesSid,
                Grants = [sharedEntry]
            }
        ]);
        var database = new AppDatabase();
        _service.LoadAdditionalConfig(path, database, _pinKey);

        _service.UnloadConfig(path, database);

        Assert.Empty(database.GetAccount(WellKnownSecuritySids.AllApplicationPackagesSid)?.Grants ?? []);
        Assert.Null(GetGrantConfigPath(
            WellKnownSecuritySids.AllApplicationPackagesSid,
            sharedEntry));
    }

    [Fact]
    public void UnloadConfig_DuplicateGrantAcrossAdditionalConfigs_PreservesGrantUntilLastOwnerRemoved()
    {
        var sid = "S-1-5-21-1-1-1-1001";
        var sharedGrant = new GrantedPathEntry { Path = @"C:\shared-grant" };
        var path1 = CreateConfigFile(accounts: [new AppConfigAccountEntry { Sid = sid, Grants = [sharedGrant.Clone()] }]);
        var path2 = CreateConfigFile(accounts: [new AppConfigAccountEntry { Sid = sid, Grants = [sharedGrant.Clone()] }]);
        var database = new AppDatabase();
        _service.LoadAdditionalConfig(path1, database, _pinKey);
        _service.LoadAdditionalConfig(path2, database, _pinKey);

        _service.UnloadConfig(path1, database);

        var accountAfterFirstUnload = database.GetAccount(sid);
        Assert.NotNull(accountAfterFirstUnload);
        Assert.Single(accountAfterFirstUnload!.Grants);
        Assert.Equal(Path.GetFullPath(path2), GetGrantConfigPath(sid, accountAfterFirstUnload.Grants[0]));

        _service.UnloadConfig(path2, database);

        Assert.Null(database.GetAccount(sid));
    }

    [Fact]
    public void LoadAdditionalConfig_DuplicateMainGrant_PreservesMainOwnershipDuringFilterAndUnload()
    {
        var sid = "S-1-5-21-1-1-1-1001";
        var sharedGrant = new GrantedPathEntry { Path = @"C:\shared-main-grant" };
        var path = CreateConfigFile(accounts: [new AppConfigAccountEntry { Sid = sid, Grants = [sharedGrant.Clone()] }]);
        var database = new AppDatabase();
        database.GetOrCreateAccount(sid).Grants.Add(sharedGrant.Clone());
        UseDatabase(database);

        _service.LoadAdditionalConfig(path, database, _pinKey);

        var filteredWhileLoaded = _index.FilterForMainConfig(database);
        var mainGrantsWhileLoaded = filteredWhileLoaded.GetAccount(sid)?.Grants;
        Assert.NotNull(mainGrantsWhileLoaded);
        Assert.Single(mainGrantsWhileLoaded!);
        Assert.Equal(sharedGrant.Path, mainGrantsWhileLoaded[0].Path);

        _service.UnloadConfig(path, database);

        var remainingGrants = database.GetAccount(sid)?.Grants;
        Assert.NotNull(remainingGrants);
        Assert.Single(remainingGrants!);
        Assert.Equal(sharedGrant.Path, remainingGrants[0].Path);
    }

    [Fact]
    public void LoadAdditionalConfig_DuplicateMainSharedTraverseGrant_PreservesMainOwnershipDuringFilterAndUnload()
    {
        var sharedEntry = new GrantedPathEntry { Path = @"C:\shared-main-traverse", IsTraverseOnly = true };
        var path = CreateConfigFile(accounts:
        [
            new AppConfigAccountEntry
            {
                Sid = WellKnownSecuritySids.AllApplicationPackagesSid,
                Grants = [sharedEntry.Clone()]
            }
        ]);
        var database = new AppDatabase();
        database.GetOrCreateAccount(WellKnownSecuritySids.AllApplicationPackagesSid).Grants.Add(sharedEntry.Clone());
        UseDatabase(database);

        _service.LoadAdditionalConfig(path, database, _pinKey);

        var filteredWhileLoaded = _index.FilterForMainConfig(database);
        var filteredAccount = Assert.Single(filteredWhileLoaded.Accounts);
        Assert.Equal(sharedEntry.Path, Assert.Single(filteredAccount.Grants).Path);

        _service.UnloadConfig(path, database);

        var remainingAccount = Assert.Single(database.Accounts);
        Assert.Equal(sharedEntry.Path, Assert.Single(remainingAccount.Grants).Path);
    }

    [Fact]
    public void SaveConfigForApp_AdditionalConfig_IncludesAllApplicationPackagesTraverseEntriesInAccounts()
    {
        var app = new AppEntry { Id = AppEntry.GenerateId(), Name = "App" };
        var sharedEntry = new GrantedPathEntry { Path = @"C:\shared", IsTraverseOnly = true };
        var path = CreateConfigFile([app], accounts:
        [
            new AppConfigAccountEntry
            {
                Sid = WellKnownSecuritySids.AllApplicationPackagesSid,
                Grants = [sharedEntry]
            }
        ]);
        var database = new AppDatabase();
        _service.LoadAdditionalConfig(path, database, _pinKey);

        _service.SaveConfigForApp(app.Id, database, _pinKey, _argonSalt);

        _dbService.Verify(d => d.SaveAppConfig(
            It.Is<AppConfig>(c =>
                c.Accounts != null &&
                c.Accounts.Count == 1 &&
                c.Accounts[0].Sid == WellKnownSecuritySids.AllApplicationPackagesSid &&
                c.Accounts[0].Grants.Single().Path == @"C:\shared" &&
                c.Accounts[0].Grants.Single().IsTraverseOnly),
            Path.GetFullPath(path),
            _pinKey, _argonSalt), Times.Once);
    }

    [Fact]
    public void SaveConfigForApp_AdditionalConfig_AllApplicationPackagesGrant_StaysInAccounts()
    {
        var app = new AppEntry { Id = AppEntry.GenerateId(), Name = "App" };
        var path = CreateConfigFile([app], accounts:
        [
            new AppConfigAccountEntry
            {
                Sid = WellKnownSecuritySids.AllApplicationPackagesSid,
                Grants =
                [
                    new GrantedPathEntry
                    {
                        Path = @"C:\aap-extra-grant",
                        SavedRights = SavedRightsState.DefaultForMode(isDeny: false)
                    }
                ]
            }
        ]);
        var database = new AppDatabase();
        _service.LoadAdditionalConfig(path, database, _pinKey);

        _service.SaveConfigForApp(app.Id, database, _pinKey, _argonSalt);

        _dbService.Verify(d => d.SaveAppConfig(
            It.Is<AppConfig>(c =>
                c.Accounts != null &&
                c.Accounts.Count == 1 &&
                c.Accounts[0].Sid == WellKnownSecuritySids.AllApplicationPackagesSid &&
                c.Accounts[0].Grants.Count == 1 &&
                c.Accounts[0].Grants[0].Path == @"C:\aap-extra-grant" &&
                !c.Accounts[0].Grants[0].IsTraverseOnly),
            Path.GetFullPath(path),
            _pinKey, _argonSalt), Times.Once);
    }

    [Fact]
    public void SaveConfigForApp_AdditionalConfig_AllApplicationPackagesGrant_DoesNotDuplicateSharedTraverseIntoAccounts()
    {
        var app = new AppEntry { Id = AppEntry.GenerateId(), Name = "App" };
        var path = CreateConfigFile(
            [app],
            accounts:
            [
                new AppConfigAccountEntry
                {
                    Sid = WellKnownSecuritySids.AllApplicationPackagesSid,
                    Grants =
                    [
                        new GrantedPathEntry
                        {
                            Path = @"C:\aap-extra-grant",
                            SavedRights = SavedRightsState.DefaultForMode(isDeny: false)
                        },
                        new GrantedPathEntry
                        {
                            Path = @"C:\aap-extra-traverse",
                            IsTraverseOnly = true
                        }
                    ]
                }
            ]);
        var database = new AppDatabase();
        _service.LoadAdditionalConfig(path, database, _pinKey);

        _service.SaveConfigForApp(app.Id, database, _pinKey, _argonSalt);

        _dbService.Verify(d => d.SaveAppConfig(
            It.Is<AppConfig>(c =>
                c.Accounts != null &&
                c.Accounts.Count == 1 &&
                c.Accounts[0].Sid == WellKnownSecuritySids.AllApplicationPackagesSid &&
                c.Accounts[0].Grants.Count == 2 &&
                c.Accounts[0].Grants.Any(grant => grant.Path == @"C:\aap-extra-grant" && !grant.IsTraverseOnly) &&
                c.Accounts[0].Grants.Any(grant => grant.Path == @"C:\aap-extra-traverse" && grant.IsTraverseOnly)),
            Path.GetFullPath(path),
            _pinKey, _argonSalt), Times.Once);
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
        var path = CreateConfigFile([app], new Dictionary<string, HandlerMappingEntry> { [".pdf"] = new HandlerMappingEntry(appId) });
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
    public void SaveConfigForApp_AdditionalConfig_IncludesOnlySelectedStoreAccountGrants_WhenMainOwnershipAlsoExists()
    {
        const string sid = "S-1-5-21-1-1-1-4001";
        var app = new AppEntry { Id = AppEntry.GenerateId(), Name = "App" };
        var sharedGrant = new GrantedPathEntry { Path = @"C:\shared" };
        var additionalOnlyGrant = new GrantedPathEntry { Path = @"C:\additional-only" };
        var mainOnlyGrant = new GrantedPathEntry { Path = @"C:\main-only" };
        var path = CreateConfigFile(
            [app],
            accounts: [new AppConfigAccountEntry
            {
                Sid = sid,
                Grants = [sharedGrant.Clone(), additionalOnlyGrant.Clone()]
            }]);
        var database = new AppDatabase();
        database.GetOrCreateAccount(sid).Grants.Add(sharedGrant.Clone());
        database.GetOrCreateAccount(sid).Grants.Add(mainOnlyGrant);
        _service.LoadAdditionalConfig(path, database, _pinKey);

        _service.SaveConfigForApp(app.Id, database, _pinKey, _argonSalt);

        _dbService.Verify(d => d.SaveAppConfig(
            It.Is<AppConfig>(c =>
                HasGrantPaths(c, sid, @"C:\additional-only", @"C:\shared") &&
                !HasGrantPath(c, sid, @"C:\main-only")),
            Path.GetFullPath(path),
            _pinKey,
            _argonSalt), Times.Once);
    }

    [Fact]
    public void SaveConfigForApp_MainConfig_CallsSaveConfig()
    {
        var app = new AppEntry { Id = AppEntry.GenerateId(), Name = "MainApp" };
        var database = new AppDatabase { Apps = [app] };

        _service.SaveConfigForApp(app.Id, database, _pinKey, _argonSalt);

        _dbService.Verify(d => d.SaveConfig(database, _pinKey, _argonSalt), Times.Once);
        _dbService.Verify(d => d.SaveAppConfig(
                It.IsAny<AppConfig>(), It.IsAny<string>(), It.IsAny<ISecureSecretSnapshotSource>(), It.IsAny<byte[]>()),
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
    public void SaveAllConfigs_AdditionalConfigPayloads_UseOnlyEachConfigGrantStore()
    {
        const string sid = "S-1-5-21-1-1-1-4002";
        var app1 = new AppEntry { Id = AppEntry.GenerateId(), Name = "App1" };
        var app2 = new AppEntry { Id = AppEntry.GenerateId(), Name = "App2" };
        var path1 = CreateConfigFile(
            [app1],
            accounts: [new AppConfigAccountEntry
            {
                Sid = sid,
                Grants =
                [
                    new GrantedPathEntry { Path = @"C:\shared" },
                    new GrantedPathEntry { Path = @"C:\path1-only" }
                ]
            }]);
        var path2 = CreateConfigFile(
            [app2],
            accounts: [new AppConfigAccountEntry
            {
                Sid = sid,
                Grants = [new GrantedPathEntry { Path = @"C:\path2-only" }]
            }]);
        var database = new AppDatabase();
        database.GetOrCreateAccount(sid).Grants.Add(new GrantedPathEntry { Path = @"C:\shared" });
        database.GetOrCreateAccount(sid).Grants.Add(new GrantedPathEntry { Path = @"C:\main-only" });
        _service.LoadAdditionalConfig(path1, database, _pinKey);
        _service.LoadAdditionalConfig(path2, database, _pinKey);

        var savedConfigs = new Dictionary<string, AppConfig>(StringComparer.OrdinalIgnoreCase);
        _dbService.Setup(d => d.SaveAppConfig(
                It.IsAny<AppConfig>(),
                It.IsAny<string>(),
                It.IsAny<ISecureSecretSnapshotSource>(),
                It.IsAny<byte[]>()))
            .Callback<AppConfig, string, ISecureSecretSnapshotSource, byte[]>((config, configPath, _, _) =>
                savedConfigs[configPath] = config);

        _service.SaveAllConfigs(database, _pinKey, _argonSalt);

        Assert.True(savedConfigs.ContainsKey(Path.GetFullPath(path1)));
        Assert.True(savedConfigs.ContainsKey(Path.GetFullPath(path2)));
        Assert.True(HasGrantPaths(savedConfigs[Path.GetFullPath(path1)], sid, @"C:\path1-only", @"C:\shared"));
        Assert.False(HasGrantPath(savedConfigs[Path.GetFullPath(path1)], sid, @"C:\main-only"));
        Assert.False(HasGrantPath(savedConfigs[Path.GetFullPath(path1)], sid, @"C:\path2-only"));
        Assert.True(HasGrantPaths(savedConfigs[Path.GetFullPath(path2)], sid, @"C:\path2-only"));
        Assert.False(HasGrantPath(savedConfigs[Path.GetFullPath(path2)], sid, @"C:\shared"));
        Assert.False(HasGrantPath(savedConfigs[Path.GetFullPath(path2)], sid, @"C:\main-only"));
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

        _service.SaveImportedConfig(path, new AppConfig { Apps = [app] }, _pinKey, _argonSalt);

        _dbService.Verify(d => d.SaveAppConfig(
            It.Is<AppConfig>(c => c.Apps.Any(a => a.Id == app.Id)),
            Path.GetFullPath(path),
            _pinKey, _argonSalt), Times.Once);
    }

    [Fact]
    public void SaveImportedConfig_InvalidAppId_ThrowsInvalidAppIdExceptionWithoutRewritingId()
    {
        var path = Path.Combine(_tempDir.Path, $"{Guid.NewGuid():N}.ramc");
        var config = new AppConfig
        {
            Apps = [new AppEntry { Id = @"..\bad", Name = "Imported" }]
        };

        var ex = Assert.Throws<InvalidAppIdException>(() =>
            _service.SaveImportedConfig(path, config, _pinKey, _argonSalt));

        Assert.Equal(@"..\bad", config.Apps[0].Id);
        Assert.Contains("invalid", ex.Message, StringComparison.OrdinalIgnoreCase);
        _dbService.Verify(d => d.SaveAppConfig(
                It.IsAny<AppConfig>(),
                It.IsAny<string>(),
                It.IsAny<ISecureSecretSnapshotSource>(),
                It.IsAny<byte[]>()),
            Times.Never);
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

        using var newKey = TestSecretFactory.Create(32);
        var store = new CredentialStore
        {
            ArgonSalt = _argonSalt,
            EncryptedCanary = [1, 2, 3]
        };

        List<(string, AppConfig)>? capturedConfigs = null;
        ISecureSecretSnapshotSource? capturedKey = null;
        _dbService
            .Setup(d => d.SaveCredentialStoreAndAllConfigs(
                It.IsAny<CredentialStore>(),
                It.IsAny<AppDatabase>(),
                It.IsAny<ISecureSecretSnapshotSource>(),
                It.IsAny<List<(string, AppConfig)>>()))
            .Callback<CredentialStore, AppDatabase, ISecureSecretSnapshotSource, List<(string, AppConfig)>>(
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
        Assert.Same(newKey, capturedKey);

        // Both additional configs must be included
        Assert.NotNull(capturedConfigs);
        Assert.Equal(2, capturedConfigs!.Count);
        Assert.Contains(capturedConfigs, c => c.Item1 == Path.GetFullPath(path1));
        Assert.Contains(capturedConfigs, c => c.Item1 == Path.GetFullPath(path2));
    }

    [Fact]
    public void ReencryptAndSaveAll_AdditionalConfigPayloads_UseOnlyEachConfigGrantStore()
    {
        const string sid = "S-1-5-21-1-1-1-4003";
        var app1 = new AppEntry { Id = AppEntry.GenerateId(), Name = "App1" };
        var app2 = new AppEntry { Id = AppEntry.GenerateId(), Name = "App2" };
        var path1 = CreateConfigFile(
            [app1],
            accounts: [new AppConfigAccountEntry
            {
                Sid = sid,
                Grants =
                [
                    new GrantedPathEntry { Path = @"C:\shared" },
                    new GrantedPathEntry { Path = @"C:\path1-only" }
                ]
            }]);
        var path2 = CreateConfigFile(
            [app2],
            accounts: [new AppConfigAccountEntry
            {
                Sid = sid,
                Grants = [new GrantedPathEntry { Path = @"C:\path2-only" }]
            }]);
        var database = new AppDatabase();
        database.GetOrCreateAccount(sid).Grants.Add(new GrantedPathEntry { Path = @"C:\shared" });
        database.GetOrCreateAccount(sid).Grants.Add(new GrantedPathEntry { Path = @"C:\main-only" });
        _service.LoadAdditionalConfig(path1, database, _pinKey);
        _service.LoadAdditionalConfig(path2, database, _pinKey);

        var store = new CredentialStore
        {
            ArgonSalt = _argonSalt,
            EncryptedCanary = [1, 2, 3]
        };
        using var newKey = TestSecretFactory.Create(32);
        List<(string, AppConfig)>? capturedConfigs = null;
        _dbService
            .Setup(d => d.SaveCredentialStoreAndAllConfigs(
                It.IsAny<CredentialStore>(),
                It.IsAny<AppDatabase>(),
                It.IsAny<ISecureSecretSnapshotSource>(),
                It.IsAny<List<(string, AppConfig)>>()))
            .Callback<CredentialStore, AppDatabase, ISecureSecretSnapshotSource, List<(string, AppConfig)>>(
                (_, _, _, configs) => capturedConfigs = configs);

        _service.ReencryptAndSaveAll(store, database, newKey);

        Assert.NotNull(capturedConfigs);
        var path1Config = capturedConfigs!.Single(c => string.Equals(c.Item1, Path.GetFullPath(path1), StringComparison.OrdinalIgnoreCase)).Item2;
        var path2Config = capturedConfigs.Single(c => string.Equals(c.Item1, Path.GetFullPath(path2), StringComparison.OrdinalIgnoreCase)).Item2;
        Assert.True(HasGrantPaths(path1Config, sid, @"C:\path1-only", @"C:\shared"));
        Assert.False(HasGrantPath(path1Config, sid, @"C:\main-only"));
        Assert.False(HasGrantPath(path1Config, sid, @"C:\path2-only"));
        Assert.True(HasGrantPaths(path2Config, sid, @"C:\path2-only"));
        Assert.False(HasGrantPath(path2Config, sid, @"C:\shared"));
        Assert.False(HasGrantPath(path2Config, sid, @"C:\main-only"));
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
        using var newKey = TestSecretFactory.Create(32);

        List<(string, AppConfig)>? capturedConfigs = null;
        _dbService
            .Setup(d => d.SaveCredentialStoreAndAllConfigs(
                It.IsAny<CredentialStore>(),
                It.IsAny<AppDatabase>(),
                It.IsAny<ISecureSecretSnapshotSource>(),
                It.IsAny<List<(string, AppConfig)>>()))
            .Callback<CredentialStore, AppDatabase, ISecureSecretSnapshotSource, List<(string, AppConfig)>>(
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
        var extraPath = CreateConfigFile([extraApp],
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
        var extraPath = CreateConfigFile([extraApp],
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
        var extraPath = CreateConfigFile([extraApp],
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
        var extraPath = CreateConfigFile([extraApp],
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
        var path = CreateConfigFile([appA, appB],
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
        var path = CreateConfigFile([app],
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
        var path = CreateConfigFile([app],
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
        var path = CreateConfigFile([app],
            new Dictionary<string, HandlerMappingEntry> { ["http"] = new HandlerMappingEntry(app.Id) });
        _service.LoadAdditionalConfig(path, database, _pinKey);

        _service.UnloadConfig(path, database);

        var effective = _handlerMappings.GetEffectiveHandlerMappings(database);
        Assert.False(effective.ContainsKey("http"));
    }

    private string? GetGrantConfigPath(string sid, GrantedPathEntry entry)
        => (entry.IsTraverseOnly
                ? _grantIntentRepository.FindTraverse(sid, entry)
                : _grantIntentRepository.FindGrant(sid, entry))
            ?.Store.ConfigPath;

    private static bool HasGrantPath(AppConfig config, string sid, string path)
        => config.Accounts?.Any(account =>
            string.Equals(account.Sid, sid, StringComparison.OrdinalIgnoreCase) &&
            account.Grants.Any(grant =>
                string.Equals(grant.Path, path, StringComparison.OrdinalIgnoreCase))) == true;

    private static bool HasGrantPaths(AppConfig config, string sid, params string[] expectedPaths)
    {
        var account = config.Accounts?.SingleOrDefault(candidate =>
            string.Equals(candidate.Sid, sid, StringComparison.OrdinalIgnoreCase));
        if (account == null)
            return false;

        var actualPaths = account.Grants
            .Select(grant => grant.Path)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var expected = expectedPaths
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return actualPaths.SequenceEqual(expected, StringComparer.OrdinalIgnoreCase);
    }
}
