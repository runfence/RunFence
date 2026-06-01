using System.Security.Cryptography;
using RunFence.Acl;
using RunFence.Apps;
using RunFence.Apps.Shortcuts;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Persistence;
using RunFence.Persistence.UI;
using RunFence.Launch.Tokens;
using RunFence.Tests;
using Xunit;

namespace RunFence.IntegrationTests;

[Collection("PrivilegeMutation")]
public sealed class ShortcutPersistenceSmokeTests
{
    [ElevatedFact]
    public void ShortcutService_UsesProductionTrustedTempPersistence_ForNewAndProtectedExistingShortcutWrites()
    {
        using var root = new TempDirectory("RunFence_ShortcutPersistenceSmoke");
        var trustedTempPath = System.IO.Path.Combine(root.Path, "trusted-temp");
        Directory.CreateDirectory(trustedTempPath);

        var shortcutPath = System.IO.Path.Combine(root.Path, "Managed App.lnk");
        var iconPath = System.IO.Path.Combine(root.Path, "managed.ico");
        File.WriteAllBytes(iconPath, []);

        using var stateStoreContext = new ProductionShortcutStateStoreContext();
        var app = new AppEntry
        {
            Id = "create-app-id",
            ExePath = Environment.ProcessPath ?? Environment.GetCommandLineArgs()[0],
            ManageShortcuts = true
        };
        var (shortcutService, shortcutHelper, persistenceNative) = stateStoreContext.CreateShortcutService(
            app,
            iconPath,
            trustedTempPath);
        try
        {
            shortcutService.SaveShortcut(app, shortcutPath);

            ShortcutPersistenceTestAssertions.AssertShortcut(
                shortcutHelper,
                shortcutPath,
                Path.Combine(AppContext.BaseDirectory, PathConstants.LauncherExeName),
                app.Id,
                Path.GetDirectoryName(Path.Combine(AppContext.BaseDirectory, PathConstants.LauncherExeName)));
            Assert.True(File.GetAttributes(shortcutPath).HasFlag(FileAttributes.ReadOnly));
            Assert.True(ShortcutPersistenceTestAssertions.HasManagedEveryoneDenyAce(shortcutPath));
            var createdShortcutIdentity = ShortcutPersistenceTestAssertions.ReadFileIdentity(shortcutPath);
            var persistedState = stateStoreContext.StateStore.Load(app.Id, shortcutPath);
            Assert.NotNull(persistedState);
            Assert.Equal(Path.GetFullPath(shortcutPath), persistedState.ShortcutPath);
            Assert.True(persistedState.ManagedDenyAceApplied);
            Assert.False(persistedState.WasReadOnlyBeforeProtection);
            Assert.True(persistedState.ReadOnlySetByRunFence);

            var launcherPath = Path.Combine(root.Path, "Launcher", "RunFence.Launcher.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(launcherPath)!);
            File.WriteAllBytes(launcherPath, []);
            var cache = new ShortcutTraversalCache(
            [
                new ShortcutTraversalEntry(
                    shortcutPath,
                    Path.Combine(AppContext.BaseDirectory, PathConstants.LauncherExeName),
                    app.Id)
            ]);
            shortcutService.EnforceShortcuts([app], launcherPath, cache);
            var updatedFileIdentity = ShortcutPersistenceTestAssertions.ReadFileIdentity(shortcutPath);

            ShortcutPersistenceTestAssertions.AssertShortcut(
                shortcutHelper,
                shortcutPath,
                launcherPath,
                app.Id,
                Path.GetDirectoryName(launcherPath));
            Assert.NotEqual(createdShortcutIdentity, updatedFileIdentity);
            Assert.True(File.GetAttributes(shortcutPath).HasFlag(FileAttributes.ReadOnly));
            Assert.True(ShortcutPersistenceTestAssertions.HasManagedEveryoneDenyAce(shortcutPath));
            Assert.Empty(Directory.GetFiles(trustedTempPath, "*.lnk", SearchOption.AllDirectories));

            using var reloadedStateStoreContext = stateStoreContext.CreateReloadedStateStoreContext();
            var reloadedState = reloadedStateStoreContext.StateStore.Load(app.Id, shortcutPath);
            Assert.NotNull(reloadedState);
            Assert.Equal(Path.GetFullPath(shortcutPath), reloadedState.ShortcutPath);
            Assert.True(reloadedState.ManagedDenyAceApplied);
            Assert.False(reloadedState.WasReadOnlyBeforeProtection);
            Assert.True(reloadedState.ReadOnlySetByRunFence);
            Assert.Equal(persistedState, reloadedState);
        }
        finally
        {
            ShortcutPersistenceTestAssertions.TryDeleteShortcut(persistenceNative, shortcutPath);
        }
    }

    [ElevatedFact]
    public void ShortcutService_ProtectedExistingShortcutWithoutRestorePrivilege_ThrowsAccessDeniedExceptionChain()
    {
        using var root = new TempDirectory("RunFence_ShortcutPersistence_NoRestorePrivilege");
        var trustedTempPath = Path.Combine(root.Path, "trusted-temp");
        Directory.CreateDirectory(trustedTempPath);

        var shortcutPath = Path.Combine(root.Path, "Managed App.lnk");
        var iconPath = Path.Combine(root.Path, "managed.ico");
        File.WriteAllBytes(iconPath, []);

        using var stateStoreContext = new ProductionShortcutStateStoreContext();
        var app = new AppEntry
        {
            Id = "no-restore-app-id",
            ExePath = Environment.ProcessPath ?? Environment.GetCommandLineArgs()[0],
            ManageShortcuts = true
        };
        var (shortcutService, shortcutHelper, persistenceNative) = stateStoreContext.CreateShortcutService(
            app,
            iconPath,
            trustedTempPath);
        try
        {
            shortcutService.SaveShortcut(app, shortcutPath);
            var originalDefinition = shortcutHelper.GetShortcutDefinition(shortcutPath);

            var launcherPath = Path.Combine(root.Path, "Launcher", "RunFence.Launcher.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(launcherPath)!);
            File.WriteAllBytes(launcherPath, []);
            var cache = new ShortcutTraversalCache(
            [
                new ShortcutTraversalEntry(
                    shortcutPath,
                    originalDefinition.TargetPath,
                    app.Id)
            ]);

            using var restorePrivilegeScope = new CurrentProcessPrivilegeScope(TokenPrivilegeHelper.SeRestorePrivilege);

            var ex = Assert.Throws<ShortcutEnforcementException>(() =>
                shortcutService.EnforceShortcuts([app], launcherPath, cache));

            Assert.NotNull(ex.InnerException);
            Assert.Contains(shortcutPath, ex.Message);
            Assert.True(ShortcutPersistenceTestAssertions.HasAccessDeniedCause(ex));
            Assert.True(ex.Causes.Count > 0);

            var finalDefinition = shortcutHelper.GetShortcutDefinition(shortcutPath);
            Assert.Equal(originalDefinition.TargetPath, finalDefinition.TargetPath);
            Assert.Equal(originalDefinition.Arguments, finalDefinition.Arguments);
            Assert.True(File.GetAttributes(shortcutPath).HasFlag(FileAttributes.ReadOnly));
            Assert.True(ShortcutPersistenceTestAssertions.HasManagedEveryoneDenyAce(shortcutPath));
        }
        finally
        {
            ShortcutPersistenceTestAssertions.TryDeleteShortcut(persistenceNative, shortcutPath);
        }
    }

    private static AclAccessor CreateAclAccessor()
        => new(
            new AclAccessorNative(),
            new BackupPrivilegeSecurityDescriptorAccessor(new BackupPrivilegeSecurityNative()));

    private static IShortcutFilePersistenceNative CreatePersistenceNative()
    {
        var destinationNativeApi = new ShortcutDestinationNativeApi();
        var backupAccessor = new BackupPrivilegeSecurityDescriptorAccessor(new BackupPrivilegeSecurityNative());
        return new ShortcutFilePersistenceNative(
            destinationNativeApi,
            new ShortcutDestinationEntryAccessor(destinationNativeApi, backupAccessor),
            backupAccessor);
    }

    private sealed class ProductionShortcutStateStoreContext : IDisposable
    {
        private readonly SecureSecret _pinKey;
        private readonly TempDirectory _tempDirectory;
        private readonly TestConfigPaths _configPaths;
        private readonly PersistenceAtomicFileWriter _atomicFileWriter;

        public ProductionShortcutStateStoreContext()
        {
            _pinKey = CreateSecret(32);
            _tempDirectory = new TempDirectory("RunFence_ProductionShortcutStateStore");
            _configPaths = new TestConfigPaths(_tempDirectory.Path);
            _atomicFileWriter = new PersistenceAtomicFileWriter(new PersistenceFileSecurityMirror());
            Session = new SessionContext
            {
                Database = new AppDatabase(),
                CredentialStore = new CredentialStore { ArgonSalt = new byte[32] }
            };
            Session.ReplacePinDerivedKey(CloneSecret(_pinKey));
            DatabaseService = new DatabaseService(
                new IntegrationTestLoggingService(),
                _configPaths,
                _atomicFileWriter,
                appFilter: null,
                allowPlaintextConfig: false);
            StateStore = CreateStoreForSession(Session);
        }

        public SessionContext Session { get; }
        public DatabaseService DatabaseService { get; }
        public ConfigShortcutProtectionStateStore StateStore { get; }

        public void Dispose()
        {
            Session.Dispose();
            _pinKey.Dispose();
            _tempDirectory.Dispose();
        }

        public (ShortcutService ShortcutService, ShortcutComHelper ShortcutHelper, IShortcutFilePersistenceNative PersistenceNative)
            CreateShortcutService(
                AppEntry app,
                string iconPath,
                string trustedTempPath)
        {
            Session.Database.Apps.Add(app);
            var shortcutHelper = new ShortcutComHelper();
            var persistenceNative = CreatePersistenceNative();
            var gateway = new ShortcutComGateway(shortcutHelper, persistenceNative);
            var aclAccessor = CreateAclAccessor();
            var protection = new ShortcutProtectionService(
                new IntegrationTestLoggingService(),
                StateStore,
                new ShortcutProtectionOwnershipCalculator(),
                new ShortcutManagedDenyAceEditor(aclAccessor),
                new InternalShortcutAclEditor(aclAccessor, new InternalShortcutAclPolicy()));
            var writeAccessService = new ShortcutWriteAccessService(
                new ShortcutFilePersistenceService(shortcutHelper, persistenceNative, trustedTempPath));
            var lifecycleService = new ManagedShortcutLifecycleService(
                persistenceNative,
                writeAccessService);
            var shortcutService = new ShortcutService(
                new IntegrationTestLoggingService(),
                new IntegrationTestIconService(iconPath),
                protection,
                StateStore,
                writeAccessService,
                lifecycleService,
                gateway,
                new IntegrationTestInteractiveUserDesktopProvider(),
                new ShortcutFinder());
            return (shortcutService, shortcutHelper, persistenceNative);
        }

        public ProductionShortcutStateStoreReloadContext CreateReloadedStateStoreContext()
        {
            var loadedDatabase = DatabaseService.LoadConfig(Session.PinDerivedKey);
            var reloadedPinKey = CloneSecret(_pinKey);
            var reloadedSession = new SessionContext
            {
                Database = loadedDatabase,
                CredentialStore = new CredentialStore
                {
                    ArgonSalt = Session.CredentialStore.ArgonSalt
                }
            };
            reloadedSession.ReplacePinDerivedKey(reloadedPinKey);

            return new ProductionShortcutStateStoreReloadContext(
                reloadedSession,
                CreateStoreForSession(reloadedSession));
        }

        private ConfigShortcutProtectionStateStore CreateStoreForSession(SessionContext session)
        {
            var sessionProvider = new LambdaSessionProvider(() => session);
            var ownershipProjection = new GrantIntentOwnershipProjectionService();
            var appConfigIndex = new AppConfigIndex(ownershipProjection, new AppIdValidator());
            var handlerMappingService = new HandlerMappingService(appConfigIndex);
            var configSaveOrchestrator = new ConfigSaveOrchestrator(
                sessionProvider,
                () => new InlineUiThreadInvoker(action => action()),
                DatabaseService,
                new UnusedAppConfigService(),
                handlerMappingService);
            var mainGrantIntentStore = new MainGrantIntentStore(
                sessionProvider,
                configSaveOrchestrator,
                ownershipProjection);
            var grantIntentStoreProvider = new GrantIntentStoreProvider(
                mainGrantIntentStore,
                configSaveOrchestrator,
                ownershipProjection);
            var appConfigService = new AppConfigService(
                new IntegrationTestLoggingService(),
                appConfigIndex,
                ownershipProjection,
                () => grantIntentStoreProvider,
                handlerMappingService,
                DatabaseService,
                new AppConfigSaveHelper(
                    () => grantIntentStoreProvider,
                    handlerMappingService,
                    DatabaseService),
                new AppEntryIdGenerator(),
                new AppIdValidator());
            return new ConfigShortcutProtectionStateStore(
                sessionProvider,
                appConfigService,
                () => new InlineUiThreadInvoker(action => action()));
        }

        private static SecureSecret CreateSecret(int length)
            => new(length, data => data.Fill(0));

        private static SecureSecret CloneSecret(SecureSecret source)
        {
            var bytes = source.TransformSnapshot(data => data.ToArray());
            try
            {
                return new SecureSecret(bytes.Length, data => bytes.CopyTo(data));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(bytes);
            }
        }
    }

    private sealed class ProductionShortcutStateStoreReloadContext : IDisposable
    {
        public ProductionShortcutStateStoreReloadContext(
            SessionContext session,
            ConfigShortcutProtectionStateStore stateStore)
        {
            Session = session;
            StateStore = stateStore;
        }

        public SessionContext Session { get; }
        public ConfigShortcutProtectionStateStore StateStore { get; }

        public void Dispose()
        {
            Session.Dispose();
        }
    }

    private sealed class LambdaSessionProvider(Func<SessionContext> getSession) : ISessionProvider
    {
        public SessionContext GetSession() => getSession();
    }

    private sealed class InlineUiThreadInvoker(Action<Action> invoke) : IUiThreadInvoker
    {
        public T Invoke<T>(Func<T> func)
        {
            T result = default!;
            invoke(() => result = func());
            return result;
        }

        public void BeginInvoke(Action action) => invoke(action);
    }

    private sealed class UnusedAppConfigService : IAppConfigService
    {
        public bool HasLoadedConfigs => throw NotUsed();
        public string? GetConfigPath(string appId) => throw NotUsed();
        public List<AppEntry> GetAppsForConfig(string path, AppDatabase database) => throw NotUsed();
        public AppConfig GetConfigForExport(string? path, AppDatabase database) => throw NotUsed();
        public IReadOnlyList<string> GetLoadedConfigPaths() => throw NotUsed();
        public AppConfigRuntimeStateSnapshot CaptureRuntimeStateSnapshot() => throw NotUsed();
        public void RestoreRuntimeStateSnapshot(AppConfigRuntimeStateSnapshot snapshot) => throw NotUsed();
        public AdditionalConfigLoadData ReadAdditionalConfig(string path, AppDatabase database, ISecureSecretSnapshotSource pinDerivedKey) => throw NotUsed();
        public AdditionalConfigLoadData ReadAdditionalConfigFromBackup(string configPath, AppConfig backupConfig, AppDatabase database) => throw NotUsed();
        public List<AppEntry> ApplyAdditionalConfig(AdditionalConfigLoadData configData, AppDatabase database) => throw NotUsed();
        public List<AppEntry> LoadAdditionalConfig(string path, AppDatabase database, ISecureSecretSnapshotSource pinDerivedKey) => throw NotUsed();
        public List<AppEntry> UnloadConfig(string path, AppDatabase database) => throw NotUsed();
        public void CreateEmptyConfig(string path, ISecureSecretSnapshotSource pinDerivedKey, byte[] argonSalt) => throw NotUsed();
        public void AssignApp(string appId, string? configPath) => throw NotUsed();
        public void RemoveApp(string appId) => throw NotUsed();
        public void SaveConfigForApp(string appId, AppDatabase database, ISecureSecretSnapshotSource pinDerivedKey, byte[] argonSalt) => throw NotUsed();
        public void SaveConfigAtPath(string configPath, AppDatabase database, ISecureSecretSnapshotSource pinDerivedKey, byte[] argonSalt) => throw NotUsed();
        public void SaveAllConfigs(AppDatabase database, ISecureSecretSnapshotSource pinDerivedKey, byte[] argonSalt) => throw NotUsed();
        public void ReencryptAndSaveAll(CredentialStore store, AppDatabase database, ISecureSecretSnapshotSource newPinDerivedKey) => throw NotUsed();
        public void SaveImportedConfig(string path, AppConfig config, ISecureSecretSnapshotSource pinDerivedKey, byte[] argonSalt) => throw NotUsed();

        private static NotSupportedException NotUsed()
            => new("Additional app config is not used by this integration test.");
    }

    private sealed class TestConfigPaths(string dir) : IConfigPaths
    {
        public string ConfigFilePath => Path.Combine(dir, "config.dat");
        public string CredentialsFilePath => Path.Combine(dir, "credentials.dat");
        public string LicenseFilePath => Path.Combine(dir, "license.dat");
        public string RememberPinFilePath => Path.Combine(dir, "startkey.dat");
    }
}
