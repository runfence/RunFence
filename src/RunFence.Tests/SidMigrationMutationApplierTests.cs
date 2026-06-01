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

public class SidMigrationMutationApplierTests
{
    [Fact]
    public void Apply_WithMappingsAndDeletes_MutatesDatabaseCredentialStoreAndState()
    {
        var oldSid = "S-1-old";
        var newSid = "S-1-new";
        var deletedSid = "S-1-delete";
        var database = new AppDatabase
        {
            Apps =
            [
                new AppEntry
                {
                    Id = "migrated-app",
                    Name = "Migrated App",
                    AccountSid = oldSid,
                    ExePath = @"C:\App.exe",
                    AllowedIpcCallers = [oldSid, deletedSid]
                },
                new AppEntry
                {
                    Id = "deleted-app",
                    Name = "Deleted App",
                    AccountSid = deletedSid,
                    ExePath = @"C:\Deleted.exe"
                }
            ],
            Accounts =
            [
                new AccountEntry
                {
                    Sid = oldSid,
                    IsIpcCaller = true
                },
                new AccountEntry
                {
                    Sid = deletedSid,
                    IsIpcCaller = true
                }
            ]
        };
        database.SidNames[oldSid] = @"MACHINE\OldUser";
        database.SidNames[deletedSid] = @"MACHINE\DeleteUser";
        database.AccountGroupSnapshots = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [deletedSid] = ["S-1-5-32-545"]
        };

        using var session = new SessionContext
        {
            Database = database,
            CredentialStore = new CredentialStore
            {
                Credentials =
                [
                    new CredentialEntry { Sid = oldSid, EncryptedPassword = [1] },
                    new CredentialEntry { Sid = deletedSid, EncryptedPassword = [2] }
                ]
            }
        }.WithPinDerivedKeyTakingOwnership(TestSecretFactory.Create(32));

        var shortcutDiscovery = new Mock<IShortcutDiscoveryService>();
        shortcutDiscovery
            .Setup(service => service.CreateTraversalCacheIfNeeded(It.IsAny<IEnumerable<AppEntry>>()))
            .Returns(new ShortcutTraversalCache([]));

        var firewallCleanupService = new Mock<IFirewallCleanupService>();
        var aclService = new Mock<IAclService>();
        var coreMutationService = new SidMigrationCoreMutationService();

        var dbAccessor = new UiThreadDatabaseAccessor(new MutableDatabaseProvider(database), () => new InlineUiThreadInvoker());
        var sidMigrationService = CreateSidMigrationService(database);
        var applier = new SidMigrationMutationApplier(
            sidMigrationService,
            new SidMigrationDataMappingPlanner(coreMutationService),
            new SidMigrationDeletionPlanner(),
            Mock.Of<ILoggingService>(),
            aclService.Object,
            shortcutDiscovery.Object,
            AppEntryEnforcementTestFactory.CreateCoordinator(
                aclService.Object,
                Mock.Of<IShortcutService>(),
                Mock.Of<IBesideTargetShortcutService>(),
                Mock.Of<IIconService>(),
                Mock.Of<ISidNameCacheService>(),
                Mock.Of<IInteractiveUserDesktopProvider>(),
                Mock.Of<IInteractiveUserSidResolver>(),
                new TestRunFenceLauncherPathProvider(@"C:\RunFence\RunFence.Launcher.exe", exists: true),
                Mock.Of<ILoggingService>()),
            firewallCleanupService.Object,
            Mock.Of<IShortcutService>(),
            Mock.Of<IBesideTargetShortcutService>(),
            Mock.Of<IOrphanedProfileService>(),
            Mock.Of<IGrantAccountCleanupService>(),
            dbAccessor);

        var state = new SidMigrationApplyState();
        applier.Apply(
            [new SidMigrationMapping(oldSid, newSid, "user")],
            [deletedSid],
            session,
            state);

        Assert.True(state.AppEnforcementApplied);
        Assert.True(state.FilesystemChangesApplied);
        Assert.True(state.ExternalMutationStarted);
        Assert.False(state.PostMutationFailure);
        Assert.Empty(state.Errors);
        Assert.Equal(
            [
                "Migrated 1 credential(s), 1 app(s), 2 IPC caller(s), 0 allow entry/entries.",
                "Deleted 1 credential(s), 1 app(s), 1 IPC caller(s)."
            ],
            state.Messages);

        var migratedApp = Assert.Single(database.Apps);
        Assert.Equal("migrated-app", migratedApp.Id);
        Assert.Equal(newSid, migratedApp.AccountSid);
        Assert.Equal([newSid], migratedApp.AllowedIpcCallers);
        Assert.Null(migratedApp.EnforcementRetryStatus);

        Assert.NotNull(database.GetAccount(newSid));
        Assert.True(database.GetAccount(newSid)!.IsIpcCaller);
        Assert.Null(database.GetAccount(oldSid));
        Assert.Null(database.GetAccount(deletedSid));
        Assert.DoesNotContain(
            database.AccountGroupSnapshots!.Keys,
            sid => string.Equals(sid, deletedSid, StringComparison.OrdinalIgnoreCase));

        Assert.Collection(
            session.CredentialStore.Credentials,
            credential => Assert.Equal(newSid, credential.Sid));

        aclService.Verify(service => service.RecomputeAllAncestorAcls(It.IsAny<IReadOnlyList<AppEntry>>()), Times.Exactly(2));
        firewallCleanupService.Verify(service => service.RemoveAllRules(oldSid), Times.Once);
        firewallCleanupService.Verify(service => service.RemoveAllRules(deletedSid), Times.Once);
    }

    [Fact]
    public void Apply_WithDeleteSids_RunsExternalCleanupBeforeDeletingSidData()
    {
        var operationLog = new List<string>();
        var deletedSid = "S-1-delete";
        var database = new AppDatabase();
        database.Accounts.Add(new AccountEntry { Sid = deletedSid });
        using var session = new SessionContext
        {
            Database = database,
            CredentialStore = new CredentialStore()
        }.WithPinDerivedKeyTakingOwnership(TestSecretFactory.Create(32));

        var shortcutDiscovery = new Mock<IShortcutDiscoveryService>();
        shortcutDiscovery
            .Setup(service => service.CreateTraversalCacheIfNeeded(It.IsAny<IEnumerable<AppEntry>>()))
            .Returns(new ShortcutTraversalCache([]));

        var sidMigrationService = new Mock<ISidMigrationService>();
        sidMigrationService
            .Setup(service => service.DeleteSidsFromAppData(It.IsAny<IReadOnlyList<string>>(), session.CredentialStore))
            .Callback(() => operationLog.Add("delete-app-data"))
            .Returns((0, 0, 0));

        var firewallCleanupService = new Mock<IFirewallCleanupService>();
        firewallCleanupService
            .Setup(service => service.RemoveAllRules(deletedSid))
            .Callback(() => operationLog.Add("firewall-cleanup"));
        var coreMutationService = new SidMigrationCoreMutationService();

        var dbAccessor = new UiThreadDatabaseAccessor(new MutableDatabaseProvider(database), () => new InlineUiThreadInvoker());
        var applier = new SidMigrationMutationApplier(
            sidMigrationService.Object,
            new SidMigrationDataMappingPlanner(coreMutationService),
            new SidMigrationDeletionPlanner(),
            Mock.Of<ILoggingService>(),
            Mock.Of<IAclService>(),
            shortcutDiscovery.Object,
            AppEntryEnforcementTestFactory.CreateCoordinator(
                Mock.Of<IAclService>(),
                Mock.Of<IShortcutService>(),
                Mock.Of<IBesideTargetShortcutService>(),
                Mock.Of<IIconService>(),
                Mock.Of<ISidNameCacheService>(),
                Mock.Of<IInteractiveUserDesktopProvider>(),
                Mock.Of<IInteractiveUserSidResolver>(),
                new TestRunFenceLauncherPathProvider(@"C:\RunFence\RunFence.Launcher.exe", exists: true),
                Mock.Of<ILoggingService>()),
            firewallCleanupService.Object,
            Mock.Of<IShortcutService>(),
            Mock.Of<IBesideTargetShortcutService>(),
            Mock.Of<IOrphanedProfileService>(),
            Mock.Of<IGrantAccountCleanupService>(),
            dbAccessor);

        applier.Apply([], [deletedSid], session, new SidMigrationApplyState());

        Assert.Equal(["firewall-cleanup", "delete-app-data"], operationLog);
    }

    [Fact]
    public void Apply_WhenFirewallCleanupFails_ContinuesAndKeepsMutationState()
    {
        var oldSid = "S-1-old";
        var newSid = "S-1-new";
        var database = new AppDatabase
        {
            Apps =
            [
                new AppEntry
                {
                    Id = "migrated-app",
                    Name = "Migrated App",
                    AccountSid = oldSid,
                    ExePath = @"C:\App.exe"
                }
            ]
        };
        using var session = new SessionContext
        {
            Database = database,
            CredentialStore = new CredentialStore()
        }.WithPinDerivedKeyTakingOwnership(TestSecretFactory.Create(32));

        var log = new Mock<ILoggingService>();
        var firewallCleanupService = new Mock<IFirewallCleanupService>();
        firewallCleanupService
            .Setup(service => service.RemoveAllRules(oldSid))
            .Throws(new InvalidOperationException("firewall failed"));

        var applier = CreateMutationApplier(
            database,
            CreateSidMigrationService(database),
            log: log.Object,
            firewallCleanupService: firewallCleanupService.Object);
        var state = new SidMigrationApplyState();

        applier.Apply([new SidMigrationMapping(oldSid, newSid, "user")], [], session, state);

        Assert.True(state.AppEnforcementApplied);
        Assert.False(state.FilesystemChangesApplied);
        Assert.True(state.ExternalMutationStarted);
        Assert.False(state.PostMutationFailure);
        Assert.Equal(
            ["Migrated 0 credential(s), 1 app(s), 0 IPC caller(s), 0 allow entry/entries."],
            state.Messages);
        Assert.Empty(state.Errors);
        Assert.Equal(newSid, Assert.Single(database.Apps).AccountSid);
        Assert.Null(database.Apps[0].EnforcementRetryStatus);
        log.Verify(
            service => service.Warn(It.Is<string>(message =>
                message.Contains(oldSid, StringComparison.Ordinal)
                && message.Contains("firewall failed", StringComparison.Ordinal))),
            Times.Once);
    }

    [Fact]
    public void Apply_WhenAppEnforcementFails_SetsRetryStatusAndMessage()
    {
        var oldSid = "S-1-old";
        var newSid = "S-1-new";
        var database = new AppDatabase
        {
            Apps =
            [
                new AppEntry
                {
                    Id = "migrated-app",
                    Name = "Migrated App",
                    AccountSid = oldSid,
                    ExePath = @"C:\App.exe",
                    ManageShortcuts = true
                }
            ]
        };
        using var session = new SessionContext
        {
            Database = database,
            CredentialStore = new CredentialStore()
        }.WithPinDerivedKeyTakingOwnership(TestSecretFactory.Create(32));

        var log = new Mock<ILoggingService>();
        var shortcutService = new Mock<IShortcutService>();
        shortcutService
            .Setup(service => service.RevertShortcuts(It.IsAny<AppEntry>(), It.IsAny<ShortcutTraversalCache>()))
            .Throws(new InvalidOperationException("shortcut revert failed"));

        var applier = CreateMutationApplier(
            database,
            CreateSidMigrationService(database),
            log: log.Object,
            shortcutService: shortcutService.Object);
        var state = new SidMigrationApplyState();

        applier.Apply([new SidMigrationMapping(oldSid, newSid, "user")], [], session, state);

        var app = Assert.Single(database.Apps);
        Assert.Equal(newSid, app.AccountSid);
        Assert.NotNull(app.EnforcementRetryStatus);
        Assert.Equal("shortcut revert failed", app.EnforcementRetryStatus!.FailureMessage);

        Assert.True(state.AppEnforcementApplied);
        Assert.False(state.FilesystemChangesApplied);
        Assert.True(state.ExternalMutationStarted);
        Assert.False(state.PostMutationFailure);
        Assert.Equal(2, state.Messages.Count);
        Assert.Equal("Migrated 0 credential(s), 1 app(s), 0 IPC caller(s), 0 allow entry/entries.", state.Messages[0]);
        Assert.Contains("App enforcement retry scheduled for 'Migrated App' (migrated-app): shortcut revert failed", state.Messages[1], StringComparison.Ordinal);
        Assert.Empty(state.Errors);
        log.Verify(
            service => service.Warn(It.Is<string>(message =>
                message.Contains("Migrated App", StringComparison.Ordinal)
                && message.Contains("shortcut revert failed", StringComparison.Ordinal))),
            Times.Once);

    }

    private static SidMigrationMutationApplier CreateMutationApplier(
        AppDatabase database,
        ISidMigrationService sidMigrationService,
        ILoggingService? log = null,
        IAclService? aclService = null,
        IShortcutService? shortcutService = null,
        IFirewallCleanupService? firewallCleanupService = null,
        IBesideTargetShortcutService? besideTargetShortcutService = null)
    {
        aclService ??= Mock.Of<IAclService>();
        shortcutService ??= Mock.Of<IShortcutService>();
        firewallCleanupService ??= Mock.Of<IFirewallCleanupService>();
        besideTargetShortcutService ??= Mock.Of<IBesideTargetShortcutService>();
        log ??= Mock.Of<ILoggingService>();

        var dbAccessor = new UiThreadDatabaseAccessor(new MutableDatabaseProvider(database), () => new InlineUiThreadInvoker());
        var shortcutDiscovery = new Mock<IShortcutDiscoveryService>();
        shortcutDiscovery
            .Setup(service => service.CreateTraversalCacheIfNeeded(It.IsAny<IEnumerable<AppEntry>>()))
            .Returns(new ShortcutTraversalCache([]));
        var coreMutationService = new SidMigrationCoreMutationService();
        return new SidMigrationMutationApplier(
            sidMigrationService,
            new SidMigrationDataMappingPlanner(coreMutationService),
            new SidMigrationDeletionPlanner(),
            log,
            aclService,
            shortcutDiscovery.Object,
            AppEntryEnforcementTestFactory.CreateCoordinator(
                aclService,
                shortcutService,
                besideTargetShortcutService,
                Mock.Of<IIconService>(),
                Mock.Of<ISidNameCacheService>(),
                Mock.Of<IInteractiveUserDesktopProvider>(),
                Mock.Of<IInteractiveUserSidResolver>(),
                new TestRunFenceLauncherPathProvider(@"C:\RunFence\RunFence.Launcher.exe", exists: true),
                log),
            firewallCleanupService,
            shortcutService,
            besideTargetShortcutService,
            Mock.Of<IOrphanedProfileService>(),
            Mock.Of<IGrantAccountCleanupService>(),
            dbAccessor);
    }

    [Fact]
    public void Apply_WithMappings_RunsMutationStepsInOrder()
    {
        var oldSid = "S-1-old";
        var newSid = "S-1-new";
        var operationLog = new List<string>();
        var database = new AppDatabase
        {
            Apps =
            [
                new AppEntry
                {
                    Id = "migrated-app",
                    Name = "Migrated App",
                    AccountSid = oldSid,
                    ExePath = @"C:\App.exe",
                    ManageShortcuts = true
                }
            ]
        };
        using var session = new SessionContext
        {
            Database = database,
            CredentialStore = new CredentialStore()
        }.WithPinDerivedKeyTakingOwnership(TestSecretFactory.Create(32));

        var sidMigrationService = new Mock<ISidMigrationService>();
        sidMigrationService
            .Setup(service => service.MigrateAppData(It.IsAny<IReadOnlyList<SidMigrationMapping>>(), session.CredentialStore))
            .Callback<IReadOnlyList<SidMigrationMapping>, CredentialStore>((_, _) =>
            {
                operationLog.Add("migrate-app-data");
                database.Apps[0].AccountSid = newSid;
            })
            .Returns(new MigrationCounts(0, 1, 0, 0));

        var firewallCleanupService = new Mock<IFirewallCleanupService>();
        firewallCleanupService
            .Setup(service => service.RemoveAllRules(oldSid))
            .Callback(() => operationLog.Add("cleanup-firewall"));

        var aclService = new Mock<IAclService>();
        aclService
            .Setup(service => service.RecomputeAllAncestorAcls(It.IsAny<IReadOnlyList<AppEntry>>()))
            .Callback(() => operationLog.Add("recompute-acls"));

        var shortcutService = new Mock<IShortcutService>();
        shortcutService
            .Setup(service => service.RevertShortcuts(
                It.Is<AppEntry>(app => app.Id == "migrated-app"),
                It.IsAny<ShortcutTraversalCache>()))
            .Callback<AppEntry, ShortcutTraversalCache>((app, _) =>
            {
                Assert.Equal(newSid, app.AccountSid);
                operationLog.Add("revert-shortcuts");
            });
        shortcutService
            .Setup(service => service.ReplaceShortcuts(
                It.Is<AppEntry>(app => app.Id == "migrated-app"),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<ShortcutTraversalCache>()))
            .Callback<AppEntry, string, string, ShortcutTraversalCache>((app, _, _, _) =>
            {
                Assert.Equal(newSid, app.AccountSid);
                operationLog.Add("replace-shortcuts");
            });
        var besideTargetShortcutService = new Mock<IBesideTargetShortcutService>();
        besideTargetShortcutService
            .Setup(service => service.RemoveBesideTargetShortcut(It.IsAny<AppEntry>()))
            .Callback(() => operationLog.Add("remove-beside-target"));

        var applier = CreateMutationApplier(
            database,
            sidMigrationService.Object,
            aclService: aclService.Object,
            shortcutService: shortcutService.Object,
            firewallCleanupService: firewallCleanupService.Object,
            besideTargetShortcutService: besideTargetShortcutService.Object);

        applier.Apply([new SidMigrationMapping(oldSid, newSid, "user")], [], session, new SidMigrationApplyState());

        Assert.Equal(
            ["migrate-app-data", "cleanup-firewall", "revert-shortcuts", "remove-beside-target", "replace-shortcuts", "recompute-acls"],
            operationLog);
    }

    private static SidMigrationService CreateSidMigrationService(AppDatabase database)
    {
        var dbProvider = new MutableDatabaseProvider(database);
        return new SidMigrationService(
            Mock.Of<ISidResolver>(),
            Mock.Of<IProfilePathResolver>(),
            new SidCleanupHelper(dbProvider),
            Mock.Of<ISidAclScanService>(),
            Mock.Of<ISidNameCacheService>(),
            Mock.Of<IInteractiveUserSidResolver>(),
            dbProvider,
            new SidMigrationCoreMutationService());
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
