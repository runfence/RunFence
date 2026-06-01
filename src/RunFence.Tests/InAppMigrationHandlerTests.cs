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

public class InAppMigrationHandlerTests
{
    [Fact]
    public async Task ApplyAsync_RollbackFailed_PreservesAllFailureDetails()
    {
        var database = new AppDatabase();
        database.Apps.Add(new AppEntry
        {
            Id = "app01",
            Name = "Original App",
            AccountSid = "S-1-old"
        });

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
                It.IsAny<ISecureSecretSnapshotSource>()))
            .Throws(new InvalidOperationException("rollback save failed"));

        var sidMigrationService = new Mock<ISidMigrationService>();
        sidMigrationService
            .Setup(service => service.MigrateAppData(It.IsAny<IReadOnlyList<SidMigrationMapping>>(), It.IsAny<CredentialStore>()))
            .Callback<IReadOnlyList<SidMigrationMapping>, CredentialStore>((mappings, credentialStore) =>
            {
                database.Apps.Single().AccountSid = mappings.Single().NewSid;
                credentialStore.Credentials.Single().Sid = mappings.Single().NewSid;
                throw new InvalidOperationException("mutation failed");
            });

        var databaseProvider = new MutableDatabaseProvider(database);
        var dbAccessor = new UiThreadDatabaseAccessor(databaseProvider, () => new InlineUiThreadInvoker());
        var aclService = Mock.Of<IAclService>();
        var coreMutationService = new SidMigrationCoreMutationService();
        var mutationApplier = new SidMigrationMutationApplier(
            sidMigrationService.Object,
            new SidMigrationDataMappingPlanner(coreMutationService),
            new SidMigrationDeletionPlanner(),
            Mock.Of<ILoggingService>(),
            aclService,
            Mock.Of<IShortcutDiscoveryService>(),
            AppEntryEnforcementTestFactory.CreateCoordinator(
                aclService,
                Mock.Of<IShortcutService>(),
                Mock.Of<IBesideTargetShortcutService>(),
                Mock.Of<IIconService>(),
                Mock.Of<ISidNameCacheService>(),
                Mock.Of<IInteractiveUserDesktopProvider>(),
                Mock.Of<IInteractiveUserSidResolver>(),
                new TestRunFenceLauncherPathProvider(@"C:\RunFence\RunFence.Launcher.exe", exists: true),
                Mock.Of<ILoggingService>()),
            Mock.Of<IFirewallCleanupService>(),
            Mock.Of<IShortcutService>(),
            Mock.Of<IBesideTargetShortcutService>(),
            Mock.Of<IOrphanedProfileService>(),
            Mock.Of<IGrantAccountCleanupService>(),
            dbAccessor);
        var appService = new SidMigrationApplicationService(
            appConfig.Object,
            Mock.Of<ILoggingService>(),
            Mock.Of<IFirewallEnforcementOrchestrator>(),
            mutationApplier,
            dbAccessor);
        var handler = new InAppMigrationHandler(appService);

        using var session = new SessionContext
        {
            Database = database,
            CredentialStore = new CredentialStore
            {
                Credentials =
                [
                    new CredentialEntry { Sid = "S-1-old" }
                ]
            }
        }.WithPinDerivedKeyTakingOwnership(TestSecretFactory.Create(32));

        var result = await handler.ApplyAsync(
            [new SidMigrationMapping("S-1-old", "S-1-new", "user")],
            [],
            session);

        Assert.False(result.Success);
        Assert.Equal("rollback save failed", result.SaveError);
        Assert.Equal(
            ["mutation failed", "Rollback save failed: rollback save failed"],
            result.Messages);
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
