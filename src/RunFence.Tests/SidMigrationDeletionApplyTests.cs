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

public class SidMigrationDeletionApplyTests
{
    private const string Sid = "S-1-5-21-0-0-0-2001";

    [Fact]
    public void Apply_DeleteSidGrantCleanupReturnsWarnings_LogsFormattedWarningsAndCompletes()
    {
        var database = new AppDatabase();
        database.GetOrCreateAccount(Sid).Grants.Add(new GrantedPathEntry { Path = @"C:\grant" });
        var warning = new GrantApplyWarning(
            GrantApplyFailureStep.PostRemoveAllSave,
            @"C:\grant",
            null,
            new InvalidOperationException("save failed"));
        var pathGrantService = new Mock<IGrantAccountCleanupService>();
        pathGrantService.Setup(service => service.RemoveAll(Sid))
            .Returns(new GrantApplyResult(
                DatabaseModified: true,
                DurableSaveCompleted: false,
                Warnings: [warning]));
        var log = new Mock<ILoggingService>();
        var sidMigrationService = new Mock<ISidMigrationService>();
        sidMigrationService.Setup(service => service.DeleteSidsFromAppData(
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<CredentialStore>()))
            .Returns((0, 0, 0));
        var shortcutDiscovery = new Mock<IShortcutDiscoveryService>();
        shortcutDiscovery
            .Setup(service => service.CreateTraversalCacheIfNeeded(It.IsAny<IEnumerable<AppEntry>>()))
            .Returns(new ShortcutTraversalCache([]));
        var dbAccessor = new UiThreadDatabaseAccessor(
            new LambdaDatabaseProvider(() => database),
            () => new LambdaUiThreadInvoker(action => action(), action => action()));
        var applier = new SidMigrationMutationApplier(
            sidMigrationService.Object,
            new SidMigrationDataMappingPlanner(new SidMigrationCoreMutationService()),
            new SidMigrationDeletionPlanner(),
            log.Object,
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
                log.Object),
            Mock.Of<IFirewallCleanupService>(),
            Mock.Of<IShortcutService>(),
            Mock.Of<IBesideTargetShortcutService>(),
            Mock.Of<IOrphanedProfileService>(),
            pathGrantService.Object,
            dbAccessor);
        using var session = new SessionContext
        {
            Database = database,
            CredentialStore = new CredentialStore()
        }.WithPinDerivedKeyTakingOwnership(TestSecretFactory.Create(32));
        var state = new SidMigrationApplyState();

        applier.Apply([], [Sid], session, state);

        log.Verify(
            service => service.Warn(It.Is<string>(message =>
                message.Contains(GrantApplyFailureFormatter.Format(warning), StringComparison.Ordinal))),
            Times.Once);
        Assert.Contains("Deleted 0 credential(s), 0 app(s), 0 IPC caller(s).", state.Messages);
    }

    [Fact]
    public void Apply_DeleteSidWorkflow_RunsDeletionStepsInOrder()
    {
        var operationLog = new List<string>();
        var database = new AppDatabase
        {
            Apps =
            [
                new AppEntry
                {
                    Id = "app-1",
                    Name = "Deleted App",
                    AccountSid = Sid,
                    ExePath = @"C:\App.exe",
                    ManageShortcuts = true,
                    RestrictAcl = false
                }
            ]
        };
        database.GetOrCreateAccount(Sid).Grants.Add(new GrantedPathEntry { Path = @"C:\grant" });

        var shortcutService = new Mock<IShortcutService>();
        shortcutService
            .Setup(service => service.RevertShortcuts(It.IsAny<AppEntry>(), It.IsAny<ShortcutTraversalCache>()))
            .Callback(() => operationLog.Add("revert-shortcuts"));
        var besideTargetShortcutService = new Mock<IBesideTargetShortcutService>();
        besideTargetShortcutService
            .Setup(service => service.RemoveBesideTargetShortcut(It.IsAny<AppEntry>()))
            .Callback(() => operationLog.Add("remove-beside-target"));
        var orphanedProfileService = new Mock<IOrphanedProfileService>();
        orphanedProfileService
            .Setup(service => service.CleanupLogonScripts(Sid))
            .Callback(() => operationLog.Add("cleanup-logon-scripts"));
        var firewallCleanupService = new Mock<IFirewallCleanupService>();
        firewallCleanupService
            .Setup(service => service.RemoveAllRules(Sid))
            .Callback(() => operationLog.Add("cleanup-firewall"));
        var pathGrantService = new Mock<IGrantAccountCleanupService>();
        pathGrantService
            .Setup(service => service.RemoveAll(Sid))
            .Callback(() => operationLog.Add("cleanup-grants"))
            .Returns(new GrantApplyResult(Warnings: []));
        var sidMigrationService = new Mock<ISidMigrationService>();
        sidMigrationService
            .Setup(service => service.DeleteSidsFromAppData(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CredentialStore>()))
            .Callback(() => operationLog.Add("delete-app-data"))
            .Returns((0, 1, 0));
        var aclService = new Mock<IAclService>();
        aclService
            .Setup(service => service.RecomputeAllAncestorAcls(It.IsAny<IReadOnlyList<AppEntry>>()))
            .Callback(() => operationLog.Add("recompute-acls"));
        var shortcutDiscovery = new Mock<IShortcutDiscoveryService>();
        shortcutDiscovery
            .Setup(service => service.CreateTraversalCacheIfNeeded(It.IsAny<IEnumerable<AppEntry>>()))
            .Returns(new ShortcutTraversalCache([]));
        var dbAccessor = new UiThreadDatabaseAccessor(
            new LambdaDatabaseProvider(() => database),
            () => new LambdaUiThreadInvoker(action => action(), action => action()));
        var applier = new SidMigrationMutationApplier(
            sidMigrationService.Object,
            new SidMigrationDataMappingPlanner(new SidMigrationCoreMutationService()),
            new SidMigrationDeletionPlanner(),
            Mock.Of<ILoggingService>(),
            aclService.Object,
            shortcutDiscovery.Object,
            AppEntryEnforcementTestFactory.CreateCoordinator(
                aclService.Object,
                shortcutService.Object,
                besideTargetShortcutService.Object,
                Mock.Of<IIconService>(),
                Mock.Of<ISidNameCacheService>(),
                Mock.Of<IInteractiveUserDesktopProvider>(),
                Mock.Of<IInteractiveUserSidResolver>(),
                new TestRunFenceLauncherPathProvider(@"C:\RunFence\RunFence.Launcher.exe", exists: true),
                Mock.Of<ILoggingService>()),
            firewallCleanupService.Object,
            shortcutService.Object,
            besideTargetShortcutService.Object,
            orphanedProfileService.Object,
            pathGrantService.Object,
            dbAccessor);
        using var session = new SessionContext
        {
            Database = database,
            CredentialStore = new CredentialStore()
        }.WithPinDerivedKeyTakingOwnership(TestSecretFactory.Create(32));

        applier.Apply([], [Sid], session, new SidMigrationApplyState());

        Assert.Equal(
            [
                "revert-shortcuts",
                "remove-beside-target",
                "cleanup-logon-scripts",
                "cleanup-firewall",
                "cleanup-grants",
                "delete-app-data",
                "recompute-acls"
            ],
            operationLog);
    }
}
