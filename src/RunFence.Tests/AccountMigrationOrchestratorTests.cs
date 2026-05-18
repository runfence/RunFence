using Moq;
using RunFence.Account;
using RunFence.Account.OrphanedProfiles;
using RunFence.Account.UI;
using RunFence.Infrastructure;
using Xunit;

namespace RunFence.Tests;

public class AccountMigrationOrchestratorTests
{
    [Fact]
    public void DeleteProfiles_WhenNoOrphanedProfiles_ShowsInfoAndDoesNotOpenDialog()
    {
        using var messageShown = new ManualResetEventSlim();
        var modalCoordinator = new Mock<IModalCoordinator>(MockBehavior.Strict);
        var orphanedProfileService = new Mock<IOrphanedProfileService>();
        orphanedProfileService
            .Setup(service => service.GetOrphanedProfiles())
            .Returns([]);

        var messageBoxService = new Mock<IAccountMessageBoxService>();
        messageBoxService
            .Setup(service => service.Show(
                null,
                "No orphaned profiles found.",
                "Delete Orphaned Profiles",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information,
                MessageBoxDefaultButton.Button1))
            .Callback(() => messageShown.Set())
            .Returns(DialogResult.OK);

        var orchestrator = new AccountMigrationOrchestrator(
            modalCoordinator.Object,
            null!,
            orphanedProfileService.Object,
            Mock.Of<IAccountLifecycleManager>(),
            messageBoxService.Object);

        orchestrator.DeleteProfiles(parent: null);

        Assert.True(messageShown.Wait(TimeSpan.FromSeconds(5)), "Timed out waiting for orphaned-profile empty-state message.");

        messageBoxService.Verify(
            service => service.Show(
                null,
                "No orphaned profiles found.",
                "Delete Orphaned Profiles",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information,
                MessageBoxDefaultButton.Button1),
            Times.Once);
        modalCoordinator.Verify(
            coordinator => coordinator.ShowModal(It.IsAny<Form>(), It.IsAny<IWin32Window?>()),
            Times.Never);
    }
}
