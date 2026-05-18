using Moq;
using RunFence.Account.OrphanedProfiles;
using RunFence.Acl;
using RunFence.Apps.Shortcuts;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Firewall;
using RunFence.Infrastructure;
using RunFence.Persistence;
using RunFence.SidMigration;
using Xunit;

namespace RunFence.Tests;

public class SidDeletionHandlerTests
{
    private const string Sid = "S-1-5-21-0-0-0-2001";

    [Fact]
    public void Apply_RemoveAllReturnsWarnings_LogsFormattedWarningsAndCompletes()
    {
        var database = new AppDatabase();
        database.GetOrCreateAccount(Sid).Grants.Add(new GrantedPathEntry { Path = @"C:\grant" });
        var snapshot = database.CreateSnapshot();
        var warning = new GrantApplyWarning(
            GrantApplyFailureStep.PostRemoveAllSave,
            @"C:\grant",
            null,
            new InvalidOperationException("save failed"));
        var pathGrantService = new Mock<IPathGrantService>();
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
        var handler = new SidDeletionHandler(
            Mock.Of<IAclService>(),
            Mock.Of<IShortcutService>(),
            Mock.Of<IBesideTargetShortcutService>(),
            Mock.Of<IOrphanedProfileService>(),
            Mock.Of<IFirewallCleanupService>(),
            pathGrantService.Object,
            sidMigrationService.Object,
            log.Object,
            new UiThreadDatabaseAccessor(
                new LambdaDatabaseProvider(() => database),
                () => new LambdaUiThreadInvoker(action => action(), action => action())));
        var messages = new List<string>();

        handler.Apply([Sid], snapshot, new CredentialStore(), new ShortcutTraversalCache([]), messages);

        log.Verify(
            service => service.Warn(It.Is<string>(message =>
                message.Contains(GrantApplyFailureFormatter.Format(warning), StringComparison.Ordinal))),
            Times.Once);
        Assert.Contains("Deleted 0 credential(s), 0 app(s), 0 IPC caller(s).", messages);
    }
}
