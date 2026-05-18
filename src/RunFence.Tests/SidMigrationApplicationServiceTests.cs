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
        var (workflow, _, saveError) = await service.ApplyAsync([new SidMigrationMapping(OldSid, NewSid, "user")], [], session);

        Assert.Equal(SidMigrationWorkflowStatus.AppliedButSaveFailed, workflow.Status);
        Assert.False(workflow.SavedDatabase);
        Assert.Equal("save failed", saveError);
        Assert.Equal(NewSid, session.Database.Apps.Single().AccountSid);
    }

    [Fact]
    public async Task ApplyAsync_Success_ReturnsSucceeded()
    {
        var (service, _) = CreateService();
        using var session = CreateSession();

        var (workflow, _, saveError) = await service.ApplyAsync([], [], session);

        Assert.Equal(SidMigrationWorkflowStatus.Succeeded, workflow.Status);
        Assert.True(workflow.SavedDatabase);
        Assert.Null(saveError);
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

        var (workflow, messages, saveError) = await service.ApplyAsync([new SidMigrationMapping(OldSid, NewSid, "user")], [], session);

        Assert.Equal(SidMigrationWorkflowStatus.Succeeded, workflow.Status);
        Assert.True(workflow.SavedDatabase);
        Assert.True(workflow.RetryStateWritten);
        Assert.Null(saveError);
        Assert.Contains(messages, message => message.Contains("retry scheduled", StringComparison.OrdinalIgnoreCase));
    }

    private static (SidMigrationApplicationService Service, AppDatabase Database) CreateService(
        ISidMigrationService? sidMigrationService = null,
        IAppConfigService? appConfig = null,
        IAclService? aclService = null,
        IShortcutDiscoveryService? shortcutDiscovery = null,
        AppDatabase? database = null)
    {
        var db = database ?? new AppDatabase();
        var databaseProvider = new MutableDatabaseProvider(db);
        var dbAccessor = new UiThreadDatabaseAccessor(databaseProvider, () => new InlineUiThreadInvoker());
        var enforcementHelper = new AppEntryEnforcementHelper(
            aclService ?? Mock.Of<IAclService>(),
            Mock.Of<IShortcutService>(),
            Mock.Of<IBesideTargetShortcutService>(),
            Mock.Of<IIconService>(),
            Mock.Of<ISidNameCacheService>(),
            Mock.Of<IInteractiveUserDesktopProvider>(),
            Mock.Of<IInteractiveUserSidResolver>(),
            Mock.Of<ILoggingService>());

        var sidDeletionHandler = new SidDeletionHandler(
            aclService ?? Mock.Of<IAclService>(),
            Mock.Of<IShortcutService>(),
            Mock.Of<IBesideTargetShortcutService>(),
            Mock.Of<IOrphanedProfileService>(),
            Mock.Of<IFirewallCleanupService>(),
            Mock.Of<IPathGrantService>(),
            sidMigrationService ?? Mock.Of<ISidMigrationService>(),
            Mock.Of<ILoggingService>(),
            dbAccessor);

        var service = new SidMigrationApplicationService(
            sidMigrationService ?? Mock.Of<ISidMigrationService>(),
            appConfig ?? Mock.Of<IAppConfigService>(),
            Mock.Of<ILoggingService>(),
            aclService ?? Mock.Of<IAclService>(),
            shortcutDiscovery ?? Mock.Of<IShortcutDiscoveryService>(),
            enforcementHelper,
            Mock.Of<IFirewallCleanupService>(),
            Mock.Of<IFirewallEnforcementOrchestrator>(),
            sidDeletionHandler,
            dbAccessor);
        return (service, db);
    }

    private static SessionContext CreateSession(AppDatabase? database = null)
        => new SessionContext
        {
            Database = database ?? new AppDatabase(),
            CredentialStore = new CredentialStore()
        }.WithOwnedPinDerivedKey(TestSecretFactory.FromBytes([1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16]));

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
