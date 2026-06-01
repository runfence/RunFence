using Moq;
using RunFence.Apps;
using RunFence.Apps.Shortcuts;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Persistence;
using RunFence.Persistence.UI;
using Xunit;

namespace RunFence.Tests;

public sealed class ConfigShortcutProtectionStateStoreTests : IDisposable
{
    private readonly SecureSecret _pinKey = TestSecretFactory.Create(32);
    private readonly SessionContext _session;
    private readonly Mock<IAppConfigService> _appConfigService = new();

    public ConfigShortcutProtectionStateStoreTests()
    {
        _session = new SessionContext
        {
            Database = new AppDatabase(),
            CredentialStore = new CredentialStore { ArgonSalt = new byte[32] }
        }.WithClonedPinDerivedKey(_pinKey);
    }

    public void Dispose()
    {
        _session.Dispose();
        _pinKey.Dispose();
    }

    [Fact]
    public void Save_MainConfigApp_UpdatesAppStateAndSavesConfig()
    {
        var app = new AppEntry { Id = "app1", Name = "App" };
        _session.Database.Apps.Add(app);
        var store = CreateStore();
        var shortcutPath = @"C:\Shortcuts\App.lnk";

        store.Save("app1", new ShortcutProtectionState(shortcutPath, true, false, true));

        var state = Assert.Single(app.ShortcutProtectionStates!);
        Assert.Equal(Path.GetFullPath(shortcutPath), state.ShortcutPath);
        _appConfigService.Verify(service => service.SaveConfigForApp(
            "app1",
            _session.Database,
            It.IsAny<ISecureSecretSnapshotSource>(),
            _session.CredentialStore.ArgonSalt), Times.Once);
    }

    [Fact]
    public void Delete_LastState_RemovesListAndSavesConfig()
    {
        var shortcutPath = @"C:\Shortcuts\App.lnk";
        var app = new AppEntry
        {
            Id = "app1",
            ShortcutProtectionStates =
            [
                new ShortcutProtectionState(Path.GetFullPath(shortcutPath), true, false, true)
            ]
        };
        _session.Database.Apps.Add(app);
        var store = CreateStore();

        store.Delete("app1", shortcutPath);

        Assert.Null(app.ShortcutProtectionStates);
        _appConfigService.Verify(service => service.SaveConfigForApp(
            "app1",
            _session.Database,
            It.IsAny<ISecureSecretSnapshotSource>(),
            _session.CredentialStore.ArgonSalt), Times.Once);
    }

    [Fact]
    public void Save_DuplicateStatesForSameShortcut_CollapsesToSingleEntry()
    {
        var shortcutPath = @"C:\Shortcuts\App.lnk";
        var normalizedPath = Path.GetFullPath(shortcutPath);
        var app = new AppEntry
        {
            Id = "app1",
            ShortcutProtectionStates =
            [
                new ShortcutProtectionState(normalizedPath, true, false, true),
                new ShortcutProtectionState(shortcutPath, false, true, false)
            ]
        };
        _session.Database.Apps.Add(app);
        var store = CreateStore();

        store.Save("app1", new ShortcutProtectionState(shortcutPath, true, false, true));

        var state = Assert.Single(app.ShortcutProtectionStates!);
        Assert.Equal(normalizedPath, state.ShortcutPath);
        Assert.True(state.ManagedDenyAceApplied);
        Assert.True(state.ReadOnlySetByRunFence);
    }

    [Fact]
    public void Delete_DuplicateStatesForSameShortcut_RemovesAllMatches()
    {
        var shortcutPath = @"C:\Shortcuts\App.lnk";
        var normalizedPath = Path.GetFullPath(shortcutPath);
        var app = new AppEntry
        {
            Id = "app1",
            ShortcutProtectionStates =
            [
                new ShortcutProtectionState(normalizedPath, true, false, true),
                new ShortcutProtectionState(shortcutPath, false, true, false)
            ]
        };
        _session.Database.Apps.Add(app);
        var store = CreateStore();

        store.Delete("app1", shortcutPath);

        Assert.Null(app.ShortcutProtectionStates);
    }

    [Fact]
    public void Save_SaveFailure_RestoresPreviousInMemoryState()
    {
        var shortcutPath = @"C:\Shortcuts\App.lnk";
        var existing = new ShortcutProtectionState(@"C:\Shortcuts\Old.lnk", true, false, true);
        var app = new AppEntry
        {
            Id = "app1",
            ShortcutProtectionStates = [existing]
        };
        _session.Database.Apps.Add(app);
        _appConfigService
            .Setup(service => service.SaveConfigForApp(
                "app1",
                _session.Database,
                It.IsAny<ISecureSecretSnapshotSource>(),
                _session.CredentialStore.ArgonSalt))
            .Throws(new IOException("save failed"));
        var store = CreateStore();

        var ex = Assert.Throws<IOException>(() =>
            store.Save("app1", new ShortcutProtectionState(shortcutPath, true, false, true)));

        Assert.Equal("save failed", ex.Message);
        Assert.Equal([existing], app.ShortcutProtectionStates);
    }

    [Fact]
    public void PruneMissingFiles_RemovesMissingAndInvalidEntries_PreservesExistingEntries()
    {
        using var tempDir = new TempDirectory("ConfigShortcutProtectionStateStore_Prune");
        var existingPath = Path.Combine(tempDir.Path, "existing.lnk");
        File.WriteAllBytes(existingPath, [0x4C, 0x00, 0x00, 0x00]);
        var missingPath = Path.Combine(tempDir.Path, "missing.lnk");
        var app = new AppEntry
        {
            Id = "app1",
            ShortcutProtectionStates =
            [
                new ShortcutProtectionState(existingPath, true, false, true),
                new ShortcutProtectionState(missingPath, true, false, true),
                new ShortcutProtectionState("C:\\\0bad", true, false, true)
            ]
        };
        _session.Database.Apps.Add(app);
        var store = CreateStore();

        store.PruneMissingFiles("app1");

        var remaining = Assert.Single(app.ShortcutProtectionStates!);
        Assert.Equal(Path.GetFullPath(existingPath), remaining.ShortcutPath);
        _appConfigService.Verify(service => service.SaveConfigForApp(
            "app1",
            _session.Database,
            It.IsAny<ISecureSecretSnapshotSource>(),
            _session.CredentialStore.ArgonSalt), Times.Once);
    }

    [Fact]
    public void Load_MissingApp_ReturnsNull_AndDelete_IsNoOp()
    {
        var store = CreateStore();

        Assert.Null(store.Load("missing", @"C:\Shortcuts\App.lnk"));
        store.Delete("missing", @"C:\Shortcuts\App.lnk");

        _appConfigService.Verify(service => service.SaveConfigForApp(
            It.IsAny<string>(),
            It.IsAny<AppDatabase>(),
            It.IsAny<ISecureSecretSnapshotSource>(),
            It.IsAny<byte[]>()), Times.Never);
    }

    [Fact]
    public void Save_MainConfigApp_EncryptedRoundTrip_PreservesShortcutProtectionState()
    {
        using var context = new RealConfigStoreContext();
        var app = new AppEntry { Id = "main1", Name = "Main", ExePath = @"C:\Main.exe" };
        context.Session.Database.Apps.Add(app);
        var shortcutPath = context.CreateShortcutFile("main-state.lnk");

        context.Store.Save(app.Id, new ShortcutProtectionState(shortcutPath, true, false, true));

        var loadedDatabase = context.DatabaseService.LoadConfig(context.Session.PinDerivedKey);
        using var reloaded = context.CreateReloadedMainContext(loadedDatabase);

        var state = reloaded.Store.Load(app.Id, shortcutPath);
        Assert.Equal(new ShortcutProtectionState(Path.GetFullPath(shortcutPath), true, false, true), state);
    }

    [Fact]
    public void Save_AdditionalConfigApp_EncryptedRoundTrip_PreservesShortcutProtectionState()
    {
        using var context = new RealConfigStoreContext();
        var app = new AppEntry { Id = "extra1", Name = "Extra", ExePath = @"C:\Extra.exe" };
        var configPath = context.CreateAdditionalConfigFile("extra.ramc", app);
        context.LoadAdditionalConfigFromBackup(configPath, new AppConfig { Apps = [app] });
        var shortcutPath = context.CreateShortcutFile("extra-state.lnk");

        context.Store.Save(app.Id, new ShortcutProtectionState(shortcutPath, true, true, false));

        var loadedConfig = context.DatabaseService.LoadAppConfigFromPath(configPath, context.Session.PinDerivedKey);
        using var reloaded = context.CreateReloadedAdditionalContext(configPath, loadedConfig);

        var state = reloaded.Store.Load(app.Id, shortcutPath);
        Assert.Equal(new ShortcutProtectionState(Path.GetFullPath(shortcutPath), true, true, false), state);
    }

    [Fact]
    public void PruneMissingFiles_AfterEncryptedReload_PersistsOnlyExistingShortcutState()
    {
        using var context = new RealConfigStoreContext();
        var app = new AppEntry { Id = "main-prune", Name = "Main", ExePath = @"C:\Main.exe" };
        context.Session.Database.Apps.Add(app);
        var existingShortcut = context.CreateShortcutFile("existing.lnk");
        var missingShortcut = context.CreateShortcutFile("missing.lnk");

        context.Store.Save(app.Id, new ShortcutProtectionState(existingShortcut, true, false, true));
        context.Store.Save(app.Id, new ShortcutProtectionState(missingShortcut, false, false, true));
        File.Delete(missingShortcut);

        context.Store.PruneMissingFiles(app.Id);

        var loadedDatabase = context.DatabaseService.LoadConfig(context.Session.PinDerivedKey);
        using var reloaded = context.CreateReloadedMainContext(loadedDatabase);

        Assert.NotNull(reloaded.Store.Load(app.Id, existingShortcut));
        Assert.Null(reloaded.Store.Load(app.Id, missingShortcut));
    }

    private ConfigShortcutProtectionStateStore CreateStore()
        => new(
            new LambdaSessionProvider(() => _session),
            _appConfigService.Object,
            () => new InlineUiThreadInvoker(action => action()));

    private sealed class RealConfigStoreContext : IDisposable
    {
        private readonly SecureSecret _pinKey;
        private readonly TempDirectory _tempDir;
        private readonly TestConfigPaths _configPaths;
        private readonly PersistenceAtomicFileWriter _atomicFileWriter;
        private readonly GrantIntentOwnershipProjectionService _ownershipProjection = new();
        private readonly AppIdValidator _appIdValidator = new();

        public RealConfigStoreContext()
        {
            _pinKey = TestSecretFactory.Create(32);
            _tempDir = new TempDirectory("RunFence_ConfigShortcutStore");
            _configPaths = new TestConfigPaths(_tempDir.Path);
            _atomicFileWriter = new PersistenceAtomicFileWriter(new PersistenceFileSecurityMirror());
            Session = new SessionContext
            {
                Database = new AppDatabase(),
                CredentialStore = new CredentialStore
                {
                    ArgonSalt = new byte[32],
                    EncryptedCanary = [1, 2, 3]
                }
            }.WithClonedPinDerivedKey(_pinKey);

            DatabaseService = new DatabaseService(
                Mock.Of<ILoggingService>(),
                _configPaths,
                _atomicFileWriter,
                appFilter: null,
                allowPlaintextConfig: false);

            StoreContext = CreateStoreContext(Session);
            Store = StoreContext.Store;
            AppConfigService = StoreContext.AppConfigService;
        }

        public SessionContext Session { get; }
        public DatabaseService DatabaseService { get; }
        public ConfigShortcutProtectionStateStore Store { get; }
        public AppConfigService AppConfigService { get; }
        private StoreContext StoreContext { get; }

        public string CreateShortcutFile(string fileName)
        {
            var path = Path.Combine(_tempDir.Path, fileName);
            File.WriteAllBytes(path, [0x4C, 0x00, 0x00, 0x00]);
            return path;
        }

        public string CreateAdditionalConfigFile(string fileName, params AppEntry[] apps)
        {
            var configPath = Path.Combine(_tempDir.Path, fileName);
            DatabaseService.SaveAppConfig(new AppConfig { Apps = apps.ToList() }, configPath, Session.PinDerivedKey, Session.CredentialStore.ArgonSalt);
            return configPath;
        }

        public void LoadAdditionalConfigFromBackup(string configPath, AppConfig backupConfig)
        {
            var staged = AppConfigService.ReadAdditionalConfigFromBackup(configPath, backupConfig, Session.Database);
            AppConfigService.ApplyAdditionalConfig(staged, Session.Database);
        }

        public RealConfigStoreReloadContext CreateReloadedMainContext(AppDatabase loadedDatabase)
            => new(StoreContext.PinKey, _configPaths, _atomicFileWriter, loadedDatabase, null);

        public RealConfigStoreReloadContext CreateReloadedAdditionalContext(string configPath, AppConfig loadedConfig)
            => new(StoreContext.PinKey, _configPaths, _atomicFileWriter, new AppDatabase(), (configPath, loadedConfig));

        public void Dispose()
        {
            StoreContext.Dispose();
            Session.Dispose();
            _pinKey.Dispose();
            _tempDir.Dispose();
        }

        private StoreContext CreateStoreContext(SessionContext session)
        {
            var sessionProvider = new LambdaSessionProvider(() => session);
            var appConfigIndex = new AppConfigIndex(_ownershipProjection, _appIdValidator);
            var handlerMappingService = new HandlerMappingService(appConfigIndex);
            var configSaveOrchestrator = new ConfigSaveOrchestrator(
                sessionProvider,
                () => new InlineUiThreadInvoker(action => action()),
                DatabaseService,
                Mock.Of<IAppConfigService>(),
                handlerMappingService);
            var mainStore = new MainGrantIntentStore(sessionProvider, configSaveOrchestrator, _ownershipProjection);
            var grantIntentStoreProvider = new GrantIntentStoreProvider(mainStore, configSaveOrchestrator, _ownershipProjection);
            var appConfigService = new AppConfigService(
                Mock.Of<ILoggingService>(),
                appConfigIndex,
                _ownershipProjection,
                () => grantIntentStoreProvider,
                handlerMappingService,
                DatabaseService,
                new AppConfigSaveHelper(
                    () => grantIntentStoreProvider,
                    handlerMappingService,
                    DatabaseService),
                new AppEntryIdGenerator(),
                _appIdValidator);
            return new StoreContext(
                TestSecretFactory.Clone(_pinKey),
                appConfigService,
                new ConfigShortcutProtectionStateStore(
                    sessionProvider,
                    appConfigService,
                    () => new InlineUiThreadInvoker(action => action())));
        }
    }

    private sealed class RealConfigStoreReloadContext : IDisposable
    {
        public RealConfigStoreReloadContext(
            SecureSecret pinKey,
            TestConfigPaths configPaths,
            PersistenceAtomicFileWriter atomicFileWriter,
            AppDatabase loadedDatabase,
            (string configPath, AppConfig config)? additionalConfig)
        {
            Session = new SessionContext
            {
                Database = loadedDatabase,
                CredentialStore = new CredentialStore
                {
                    ArgonSalt = new byte[32],
                    EncryptedCanary = [1, 2, 3]
                }
            }.WithClonedPinDerivedKey(pinKey);

            var ownershipProjection = new GrantIntentOwnershipProjectionService();
            var appIdValidator = new AppIdValidator();
            var sessionProvider = new LambdaSessionProvider(() => Session);
            var appConfigIndex = new AppConfigIndex(ownershipProjection, appIdValidator);
            var handlerMappingService = new HandlerMappingService(appConfigIndex);
            var databaseService = new DatabaseService(
                Mock.Of<ILoggingService>(),
                configPaths,
                atomicFileWriter,
                appFilter: null,
                allowPlaintextConfig: false);
            var configSaveOrchestrator = new ConfigSaveOrchestrator(
                sessionProvider,
                () => new InlineUiThreadInvoker(action => action()),
                databaseService,
                Mock.Of<IAppConfigService>(),
                handlerMappingService);
            var mainStore = new MainGrantIntentStore(sessionProvider, configSaveOrchestrator, ownershipProjection);
            var grantIntentStoreProvider = new GrantIntentStoreProvider(mainStore, configSaveOrchestrator, ownershipProjection);
            var appConfigService = new AppConfigService(
                Mock.Of<ILoggingService>(),
                appConfigIndex,
                ownershipProjection,
                () => grantIntentStoreProvider,
                handlerMappingService,
                databaseService,
                new AppConfigSaveHelper(
                    () => grantIntentStoreProvider,
                    handlerMappingService,
                    databaseService),
                new AppEntryIdGenerator(),
                appIdValidator);

            if (additionalConfig is { } extra)
            {
                var staged = appConfigService.ReadAdditionalConfigFromBackup(extra.configPath, extra.config, Session.Database);
                appConfigService.ApplyAdditionalConfig(staged, Session.Database);
            }

            Store = new ConfigShortcutProtectionStateStore(
                sessionProvider,
                appConfigService,
                () => new InlineUiThreadInvoker(action => action()));
        }

        public SessionContext Session { get; }
        public ConfigShortcutProtectionStateStore Store { get; }

        public void Dispose()
        {
            Session.Dispose();
        }
    }

    private sealed class StoreContext(SecureSecret pinKey, AppConfigService appConfigService, ConfigShortcutProtectionStateStore store) : IDisposable
    {
        public SecureSecret PinKey { get; } = pinKey;
        public AppConfigService AppConfigService { get; } = appConfigService;
        public ConfigShortcutProtectionStateStore Store { get; } = store;

        public void Dispose() => PinKey.Dispose();
    }

    private sealed class TestConfigPaths(string dir) : IConfigPaths
    {
        public string ConfigFilePath => Path.Combine(dir, "config.dat");
        public string CredentialsFilePath => Path.Combine(dir, "credentials.dat");
        public string LicenseFilePath => Path.Combine(dir, "license.dat");
        public string RememberPinFilePath => Path.Combine(dir, "startkey.dat");
    }
}
