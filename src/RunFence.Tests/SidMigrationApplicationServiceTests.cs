using Moq;
using RunFence.Account;
using RunFence.Account.OrphanedProfiles;
using RunFence.Acl;
using RunFence.Apps;
using RunFence.Apps.Shortcuts;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Firewall;
using RunFence.Infrastructure;
using RunFence.Persistence;
using RunFence.SidMigration;
using Xunit;

namespace RunFence.Tests;

public class SidMigrationApplicationServiceTests
{
    private const string OldSid = "S-1-5-21-1-2-3-1001";
    private const string NewSid = "S-1-5-21-1-2-3-1002";

    [Fact]
    public async Task ApplyAsync_FinalSaveFailsAfterMigration_ReturnsAppliedButSaveFailedAndPreservesMutatedState()
    {
        var database = new AppDatabase();
        database.Apps.Add(new AppEntry
        {
            Id = "app01",
            Name = "Migrated App",
            AccountSid = OldSid,
            ExePath = @"C:\App.exe"
        });

        var appConfig = new Mock<IAppConfigService>();
        appConfig
            .Setup(s => s.ReencryptAndSaveAll(It.IsAny<CredentialStore>(), It.IsAny<AppDatabase>(), It.IsAny<ISecureSecretSnapshotSource>()))
            .Throws(new InvalidOperationException("save failed"));

        var sidMigrationService = new Mock<ISidMigrationService>();
        sidMigrationService
            .Setup(s => s.MigrateAppData(It.IsAny<IReadOnlyList<SidMigrationMapping>>(), It.IsAny<CredentialStore>()))
            .Callback<IReadOnlyList<SidMigrationMapping>, CredentialStore>((mappings, _) =>
            {
                database.Apps.Single().AccountSid = mappings.Single().NewSid;
            })
            .Returns(new MigrationCounts(0, 1, 0, 0));

        var (service, _) = CreateService(
            sidMigrationService: sidMigrationService.Object,
            appConfig: appConfig.Object,
            database: database);

        using var session = CreateSession(database);
        var result = await service.ApplyAsync([new SidMigrationMapping(OldSid, NewSid, "user")], [], session);

        Assert.Equal(SidMigrationWorkflowStatus.AppliedButSaveFailed, result.Workflow.Status);
        Assert.False(result.Workflow.SavedDatabase);
        Assert.Equal("save failed", result.SaveError);
        Assert.Equal(NewSid, session.Database.Apps.Single().AccountSid);
    }

    [Fact]
    public async Task ApplyAsync_Success_ReturnsSucceeded()
    {
        var (service, _) = CreateService();
        using var session = CreateSession();

        var result = await service.ApplyAsync([], [], session);

        Assert.Equal(SidMigrationWorkflowStatus.Succeeded, result.Workflow.Status);
        Assert.True(result.Workflow.SavedDatabase);
        Assert.Null(result.SaveError);
    }

    [Fact]
    public async Task ApplyAsync_PreMutationFailure_RestoresStateFromRuntimeSnapshotAndReturnsFailed()
    {
        var database = new AppDatabase();
        database.Apps.Add(new AppEntry
        {
            Id = "app01",
            Name = "Original App",
            AccountSid = OldSid
        });

        var appConfig = CreateAppConfigMock();
        var sidMigrationService = new Mock<ISidMigrationService>();
        sidMigrationService
            .Setup(service => service.MigrateAppData(It.IsAny<IReadOnlyList<SidMigrationMapping>>(), It.IsAny<CredentialStore>()))
            .Callback<IReadOnlyList<SidMigrationMapping>, CredentialStore>((mappings, credentialStore) =>
            {
                Assert.Equal([new SidMigrationMapping(OldSid, NewSid, "user")], mappings);
                Assert.Equal(OldSid, credentialStore.Credentials.Single().Sid);
                database.Apps.Single().AccountSid = NewSid;
                credentialStore.Credentials.Single().Sid = NewSid;
                throw new InvalidOperationException("mutation failed");
            });

        var (service, _) = CreateService(
            sidMigrationService: sidMigrationService.Object,
            appConfig: appConfig.Object,
            database: database);

        using var session = CreateSession(database);
        session.CredentialStore.Credentials.Add(new CredentialEntry { Sid = OldSid });

        var result = await service.ApplyAsync([new SidMigrationMapping(OldSid, NewSid, "user")], [], session);

        Assert.Equal(SidMigrationWorkflowStatus.Failed, result.Workflow.Status);
        Assert.False(result.Workflow.SavedDatabase);
        Assert.Null(result.SaveError);
        Assert.Equal(OldSid, session.Database.Apps.Single().AccountSid);
        Assert.Equal(OldSid, session.CredentialStore.Credentials.Single().Sid);
        appConfig.Verify(service => service.RestoreRuntimeStateSnapshot(It.IsAny<AppConfigRuntimeStateSnapshot>()), Times.Once);
        appConfig.Verify(service => service.ReencryptAndSaveAll(session.CredentialStore, session.Database, session.PinDerivedKey), Times.Once);
    }

    [Fact]
    public async Task ApplyAsync_AppEnforcementFailureAfterMutation_PersistsMigrationAndRetryWarning()
    {
        var sharedDatabase = new AppDatabase();
        var sidMigrationService = new Mock<ISidMigrationService>();
        sidMigrationService
            .Setup(s => s.MigrateAppData(It.IsAny<IReadOnlyList<SidMigrationMapping>>(), It.IsAny<CredentialStore>()))
            .Callback<IReadOnlyList<SidMigrationMapping>, CredentialStore>((mappings, _) =>
            {
                var map = mappings.Single();
                foreach (var app in sharedDatabase.Apps.Where(a => string.Equals(a.AccountSid, map.OldSid, StringComparison.OrdinalIgnoreCase)))
                    app.AccountSid = map.NewSid;
            })
            .Returns(new MigrationCounts(0, 1, 0, 0));

        var aclService = new Mock<IAclService>();
        aclService
            .Setup(s => s.RevertAcl(It.IsAny<AppEntry>(), It.IsAny<IReadOnlyList<AppEntry>>()))
            .Throws(new InvalidOperationException("acl enforcement failed"));
        aclService
            .Setup(s => s.RecomputeAllAncestorAcls(It.IsAny<IReadOnlyList<AppEntry>>()));

        var appConfig = new Mock<IAppConfigService>();
        appConfig.Setup(s => s.ReencryptAndSaveAll(It.IsAny<CredentialStore>(), It.IsAny<AppDatabase>(), It.IsAny<ISecureSecretSnapshotSource>()));

        var shortcutDiscovery = new Mock<IShortcutDiscoveryService>();
        shortcutDiscovery
            .Setup(s => s.CreateTraversalCacheIfNeeded(It.IsAny<IEnumerable<AppEntry>>()))
            .Returns(new ShortcutTraversalCache([]));

        var (service, database) = CreateService(
            sidMigrationService: sidMigrationService.Object,
            appConfig: appConfig.Object,
            aclService: aclService.Object,
            shortcutDiscovery: shortcutDiscovery.Object,
            database: sharedDatabase);

        using var session = CreateSession(database);
        session.Database.Apps.Add(new AppEntry
        {
            Id = "app01",
            Name = "Migrated App",
            AccountSid = OldSid,
            ExePath = @"C:\Missing\app.exe",
            RestrictAcl = true,
            ManageShortcuts = false
        });

        var result = await service.ApplyAsync([new SidMigrationMapping(OldSid, NewSid, "user")], [], session);

        Assert.Equal(SidMigrationWorkflowStatus.Succeeded, result.Workflow.Status);
        Assert.True(result.Workflow.SavedDatabase);
        Assert.True(result.Workflow.RetryStateWritten);
        Assert.Null(result.SaveError);
        Assert.Contains(result.Messages, message => message.Contains("retry scheduled", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ApplyAsync_FirewallEnforcementFailures_AddWarningAndKeepSucceeded()
    {
        var firewallEnforcementOrchestrator = new Mock<IFirewallEnforcementOrchestrator>();
        firewallEnforcementOrchestrator
            .Setup(orchestrator => orchestrator.EnforceAll(It.IsAny<AppDatabase>()))
            .Returns(new EnforceAllResult([new FirewallEnforcementFailure(FirewallEnforcementLayer.AccountRules, OldSid, "firewall warning")]));

        var (service, _) = CreateService(
            firewallEnforcementOrchestrator: firewallEnforcementOrchestrator.Object);

        using var session = CreateSession();
        var result = await service.ApplyAsync([], [], session);

        Assert.Equal(SidMigrationWorkflowStatus.Succeeded, result.Workflow.Status);
        Assert.Null(result.SaveError);
        Assert.Contains(result.Messages, message => message.Contains("Firewall enforcement warning after SID migration: AccountRules", StringComparison.Ordinal));
    }

    private static (SidMigrationApplicationService Service, AppDatabase Database) CreateService(
        ISidMigrationService? sidMigrationService = null,
        IAppConfigService? appConfig = null,
        IAclService? aclService = null,
        IShortcutDiscoveryService? shortcutDiscovery = null,
        IFirewallEnforcementOrchestrator? firewallEnforcementOrchestrator = null,
        AppDatabase? database = null)
    {
        var db = database ?? new AppDatabase();
        var databaseProvider = new MutableDatabaseProvider(db);
        var dbAccessor = new UiThreadDatabaseAccessor(databaseProvider, () => new InlineUiThreadInvoker());
        var resolvedSidMigrationService = sidMigrationService ?? Mock.Of<ISidMigrationService>();
        var resolvedAclService = aclService ?? Mock.Of<IAclService>();
        var resolvedShortcutDiscovery = shortcutDiscovery ?? Mock.Of<IShortcutDiscoveryService>();
        var enforcementCoordinator = AppEntryEnforcementTestFactory.CreateCoordinator(
            resolvedAclService,
            Mock.Of<IShortcutService>(),
            Mock.Of<IBesideTargetShortcutService>(),
            Mock.Of<IIconService>(),
            Mock.Of<ISidNameCacheService>(),
            Mock.Of<IInteractiveUserDesktopProvider>(),
            Mock.Of<IInteractiveUserSidResolver>(),
            new TestRunFenceLauncherPathProvider(@"C:\RunFence\RunFence.Launcher.exe", exists: true),
            Mock.Of<ILoggingService>());

        var shortcutService = Mock.Of<IShortcutService>();
        var besideTargetShortcutService = Mock.Of<IBesideTargetShortcutService>();
        var orphanedProfileService = Mock.Of<IOrphanedProfileService>();
        var pathGrantService = Mock.Of<IGrantAccountCleanupService>();

        var resolvedAppConfig = appConfig ?? CreateAppConfigMock().Object;
        var coreMutationService = new SidMigrationCoreMutationService();
        var resolvedMutationApplier = new SidMigrationMutationApplier(
            resolvedSidMigrationService,
            new SidMigrationDataMappingPlanner(coreMutationService),
            new SidMigrationDeletionPlanner(),
            Mock.Of<ILoggingService>(),
            resolvedAclService,
            resolvedShortcutDiscovery,
            enforcementCoordinator,
            Mock.Of<IFirewallCleanupService>(),
            shortcutService,
            besideTargetShortcutService,
            orphanedProfileService,
            pathGrantService,
            dbAccessor);

        var service = new SidMigrationApplicationService(
            resolvedAppConfig,
            Mock.Of<ILoggingService>(),
            firewallEnforcementOrchestrator ?? Mock.Of<IFirewallEnforcementOrchestrator>(),
            resolvedMutationApplier,
            dbAccessor);
        return (service, db);
    }

    private static SessionContext CreateSession(AppDatabase? database = null)
        => new SessionContext
        {
            Database = database ?? new AppDatabase(),
            CredentialStore = new CredentialStore()
        }.WithPinDerivedKeyTakingOwnership(TestSecretFactory.FromBytes([1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16]));

    private static Mock<IAppConfigService> CreateAppConfigMock()
    {
        var appConfig = new Mock<IAppConfigService>();
        appConfig
            .Setup(service => service.CaptureRuntimeStateSnapshot())
            .Returns(new AppConfigRuntimeStateSnapshot(
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                [],
                new Dictionary<string, IReadOnlyDictionary<string, HandlerMappingEntry>>(StringComparer.OrdinalIgnoreCase)));
        appConfig
            .Setup(service => service.RestoreRuntimeStateSnapshot(It.IsAny<AppConfigRuntimeStateSnapshot>()));
        appConfig
            .Setup(service => service.ReencryptAndSaveAll(
                It.IsAny<CredentialStore>(),
                It.IsAny<AppDatabase>(),
                It.IsAny<ISecureSecretSnapshotSource>()));
        return appConfig;
    }

    private sealed class MutableDatabaseProvider(AppDatabase database) : IDatabaseProvider
    {
        public AppDatabase GetDatabase() => database;
    }

    private sealed class InlineUiThreadInvoker : IUiThreadInvoker
    {
        public T Invoke<T>(Func<T> action) => action();
        public void Invoke(Action action) => action();
        public void BeginInvoke(Action action) => action();
    }
}
