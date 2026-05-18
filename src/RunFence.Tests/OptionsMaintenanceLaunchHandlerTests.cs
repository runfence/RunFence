using Moq;
using RunFence.Apps.UI;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Launch;
using RunFence.UI;
using Xunit;

namespace RunFence.Tests;

public class OptionsMaintenanceLaunchHandlerTests
{
    [Fact]
    public void OpenLogFile_LaunchesCurrentAccountLogViewerWithAssociationRedirection()
    {
        var log = new Mock<ILoggingService>();
        log.SetupGet(x => x.LogFilePath).Returns(@"C:\Logs\runfence.log");

        var launchFacade = new Mock<ILaunchFacade>();
        var launchResult = new LaunchExecutionResult(LaunchExecutionStatus.ProcessStarted, null);
        ProcessLaunchTarget? capturedTarget = null;
        LaunchIdentity? capturedIdentity = null;
        launchFacade
            .Setup(x => x.LaunchFile(
                It.IsAny<string>(),
                It.IsAny<LaunchIdentity>(),
                It.IsAny<string?>(),
                It.IsAny<Func<string, string, bool>?>()))
            .Callback<string, LaunchIdentity, string?, Func<string, string, bool>?>((path, identity, _, _) =>
            {
                capturedTarget = new ProcessLaunchTarget(path);
                capturedIdentity = identity;
            })
            .Returns(launchResult);

        var feedbackPresenter = new Mock<ILaunchFeedbackPresenter>();
        var messageBoxService = new Mock<IMessageBoxService>();
        var handler = new OptionsMaintenanceLaunchHandler(
            log.Object,
            launchFacade.Object,
            feedbackPresenter.Object,
            messageBoxService.Object);

        handler.OpenLogFile();

        Assert.Equal(@"C:\Logs\runfence.log", capturedTarget?.ExePath);
        var accountIdentity = Assert.IsType<AccountLaunchIdentity>(capturedIdentity);
        Assert.Equal(PrivilegeLevel.Isolated, accountIdentity.PrivilegeLevel);
        Assert.Equal(AssociationResolutionPolicy.AllowAccountRedirection, accountIdentity.AssociationResolutionPolicy);
        feedbackPresenter.Verify(
            x => x.ShowMaintenanceWarning(
                launchResult,
                It.Is<LaunchFeedbackContext>(ctx =>
                    ctx.StartedItem == "The log viewer" &&
                    ctx.Source == LaunchFeedbackSource.InteractiveUi)),
            Times.Once);
        messageBoxService.Verify(
            x => x.Show(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<MessageBoxButtons>(), It.IsAny<MessageBoxIcon>()),
            Times.Never);
    }

    [Fact]
    public void OpenLogFile_WhenLaunchFails_ShowsCurrentErrorText()
    {
        var log = new Mock<ILoggingService>();
        log.SetupGet(x => x.LogFilePath).Returns(@"C:\Logs\runfence.log");

        var launchFacade = new Mock<ILaunchFacade>();
        launchFacade
            .Setup(x => x.LaunchFile(
                It.IsAny<string>(),
                It.IsAny<LaunchIdentity>(),
                It.IsAny<string?>(),
                It.IsAny<Func<string, string, bool>?>()))
            .Throws(new InvalidOperationException("boom"));

        var feedbackPresenter = new Mock<ILaunchFeedbackPresenter>();
        var messageBoxService = new Mock<IMessageBoxService>();
        var handler = new OptionsMaintenanceLaunchHandler(
            log.Object,
            launchFacade.Object,
            feedbackPresenter.Object,
            messageBoxService.Object);

        handler.OpenLogFile();

        feedbackPresenter.Verify(
            x => x.ShowMaintenanceWarning(
                It.IsAny<LaunchExecutionResult>(),
                It.IsAny<LaunchFeedbackContext>()),
            Times.Never);
        messageBoxService.Verify(
            x => x.Show(
                "Failed to open log file: boom",
                "Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error),
            Times.Once);
    }
}
