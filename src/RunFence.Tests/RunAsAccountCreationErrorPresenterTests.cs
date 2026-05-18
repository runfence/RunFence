using Moq;
using RunFence.Account.UI;
using RunFence.RunAs;
using Xunit;

namespace RunFence.Tests;

public sealed class RunAsAccountCreationErrorPresenterTests
{
    [Fact]
    public void ShowCleanupStateSaveFailed_ShowsExpectedWarning()
    {
        var messageBoxService = new Mock<IAccountMessageBoxService>();
        var presenter = new RunAsAccountCreationErrorPresenter(messageBoxService.Object);

        presenter.ShowCleanupStateSaveFailed("save failed");

        messageBoxService.Verify(
            m => m.Show(
                null,
                "Windows created the account, but RunFence could not save its cleanup state.\n\n" +
                "The account remains in memory for this session only:\nsave failed",
                "Account Created But Not Saved",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button1),
            Times.Once);
    }

    [Fact]
    public void ShowCredentialSaveRolledBack_ShowsExpectedError()
    {
        var messageBoxService = new Mock<IAccountMessageBoxService>();
        var presenter = new RunAsAccountCreationErrorPresenter(messageBoxService.Object);

        presenter.ShowCredentialSaveRolledBack("save failed");

        messageBoxService.Verify(
            m => m.Show(
                null,
                "Windows created the account, but RunFence could not save the credential store.\n\n" +
                "The account was rolled back:\n" +
                "save failed",
                "Account Creation Failed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error,
                MessageBoxDefaultButton.Button1),
            Times.Once);
    }

    [Fact]
    public void ShowCredentialSaveRollbackFailed_ShowsExpectedError()
    {
        var messageBoxService = new Mock<IAccountMessageBoxService>();
        var presenter = new RunAsAccountCreationErrorPresenter(messageBoxService.Object);

        presenter.ShowCredentialSaveRollbackFailed("save failed", "rollback failed");

        messageBoxService.Verify(
            m => m.Show(
                null,
                "Windows created the account, but RunFence could not save the credential store and rollback also failed.\n\n" +
                "Save error: save failed\n" +
                "Rollback error: rollback failed",
                "Account Creation Failed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error,
                MessageBoxDefaultButton.Button1),
            Times.Once);
    }

    [Fact]
    public void ShowPrePersistenceRolledBack_ShowsExpectedError()
    {
        var messageBoxService = new Mock<IAccountMessageBoxService>();
        var presenter = new RunAsAccountCreationErrorPresenter(messageBoxService.Object);

        presenter.ShowPrePersistenceRolledBack("encrypt failed");

        messageBoxService.Verify(
            m => m.Show(
                null,
                "Windows created the account, but RunFence failed before credential persistence completed.\n\n" +
                "The account was rolled back:\n" +
                "encrypt failed",
                "Account Creation Failed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error,
                MessageBoxDefaultButton.Button1),
            Times.Once);
    }

    [Fact]
    public void ShowPrePersistenceRollbackFailed_ShowsExpectedError()
    {
        var messageBoxService = new Mock<IAccountMessageBoxService>();
        var presenter = new RunAsAccountCreationErrorPresenter(messageBoxService.Object);

        presenter.ShowPrePersistenceRollbackFailed("encrypt failed", "rollback failed");

        messageBoxService.Verify(
            m => m.Show(
                null,
                "Windows created the account, but RunFence failed before credential persistence completed and rollback also failed.\n\n" +
                "Error: encrypt failed\n" +
                "Rollback error: rollback failed",
                "Account Creation Failed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error,
                MessageBoxDefaultButton.Button1),
            Times.Once);
    }

    [Fact]
    public void ShowPostSetupWarnings_ShowsExpectedWarning()
    {
        var messageBoxService = new Mock<IAccountMessageBoxService>();
        var presenter = new RunAsAccountCreationErrorPresenter(messageBoxService.Object);

        presenter.ShowPostSetupWarnings(["warning 1", "warning 2"]);

        messageBoxService.Verify(
            m => m.Show(
                null,
                "Account created with warnings:\n\nwarning 1\nwarning 2",
                "RunFence",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button1),
            Times.Once);
    }

    [Fact]
    public void ShowPostSetupWarnings_WithNoWarnings_DoesNothing()
    {
        var messageBoxService = new Mock<IAccountMessageBoxService>();
        var presenter = new RunAsAccountCreationErrorPresenter(messageBoxService.Object);

        presenter.ShowPostSetupWarnings([]);

        messageBoxService.Verify(
            m => m.Show(
                It.IsAny<IWin32Window?>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<MessageBoxButtons>(),
                It.IsAny<MessageBoxIcon>(),
                It.IsAny<MessageBoxDefaultButton>()),
            Times.Never);
    }
}
