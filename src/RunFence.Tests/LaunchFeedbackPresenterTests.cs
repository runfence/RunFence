using Moq;
using RunFence.Account.UI;
using RunFence.Acl;
using RunFence.Core;
using RunFence.Infrastructure;
using RunFence.Launch;
using RunFence.UI;
using Xunit;

namespace RunFence.Tests;

public class LaunchFeedbackPresenterTests
{
    [Fact]
    public void ShowMaintenanceWarning_WhenInteractiveSource_ShowsMessageBox()
    {
        var messageBox = new Mock<IAccountMessageBoxService>();
        var trayBalloon = new Mock<ITrayBalloonService>();
        var log = new Mock<ILoggingService>();
        var presenter = CreatePresenter(
            log.Object,
            messageBox.Object,
            trayBalloon.Object);

        var result = new LaunchExecutionResult(
            LaunchExecutionStatus.ProcessStartedWithMaintenanceWarnings,
            null,
            ["folder handler cleanup failed"]);

        presenter.ShowMaintenanceWarning(result, new LaunchFeedbackContext("The application", LaunchFeedbackSource.InteractiveUi)
        {
            SummaryName = "Browser"
        });

        messageBox.Verify(
            m => m.Show(
                null,
                It.Is<string>(text => text.Contains("folder handler cleanup failed", StringComparison.Ordinal)),
                "RunFence",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button1),
            Times.Once);
        trayBalloon.Verify(t => t.ShowWarning(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void ShowMaintenanceWarning_WhenSilentIpcSource_ShowsTraySummary()
    {
        var messageBox = new Mock<IAccountMessageBoxService>();
        var trayBalloon = new Mock<ITrayBalloonService>();
        var log = new Mock<ILoggingService>();
        var presenter = CreatePresenter(
            log.Object,
            messageBox.Object,
            trayBalloon.Object);

        var result = new LaunchExecutionResult(
            LaunchExecutionStatus.ProcessStartedWithMaintenanceWarnings,
            null,
            ["association refresh failed"]);

        presenter.ShowMaintenanceWarning(result, new LaunchFeedbackContext("The application", LaunchFeedbackSource.SilentIpc)
        {
            SummaryName = "Browser"
        });

        messageBox.Verify(m => m.Show(It.IsAny<IWin32Window?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<MessageBoxButtons>(), It.IsAny<MessageBoxIcon>(), It.IsAny<MessageBoxDefaultButton>()), Times.Never);
        trayBalloon.Verify(t => t.ShowWarning("Browser started with warnings."), Times.Once);
    }

    [Fact]
    public void ShowGrantFailure_WhenInteractiveSourceAndRunAsContext_UsesRunAsWording()
    {
        var messageBox = new Mock<IAccountMessageBoxService>();
        var trayBalloon = new Mock<ITrayBalloonService>();
        var log = new Mock<ILoggingService>();
        var presenter = CreatePresenter(
            log.Object,
            messageBox.Object,
            trayBalloon.Object);

        var exception = new GrantOperationException(
            GrantApplyFailureStep.GrantIntentSave,
            @"C:\Apps\App.exe",
            null,
            new InvalidOperationException("disk full"));

        presenter.ShowGrantFailure(exception, new LaunchFeedbackContext("The application", LaunchFeedbackSource.InteractiveUi)
        {
            SummaryName = "App.exe",
            GrantFailureSubject = @"C:\Apps\App.exe",
            UseRunAsGrantFailureWording = true,
            FailureCaption = "RunFence"
        });

        messageBox.Verify(
            m => m.Show(
                null,
                It.Is<string>(text => text.Contains("required to launch 'C:\\Apps\\App.exe'", StringComparison.Ordinal)),
                "RunFence",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error,
                MessageBoxDefaultButton.Button1),
            Times.Once);
        trayBalloon.Verify(t => t.ShowWarning(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void ShowMaintenanceWarning_WhenInteractiveSourceAndOwnerVisible_ShowsMessageBox()
    {
        var messageBox = new Mock<IAccountMessageBoxService>();
        var trayBalloon = new Mock<ITrayBalloonService>();
        var log = new Mock<ILoggingService>();
        using var owner = new Panel
        {
            Visible = true
        };
        var handle = owner.Handle;

        var presenter = CreatePresenter(
            log.Object,
            messageBox.Object,
            trayBalloon.Object);

        var result = new LaunchExecutionResult(
            LaunchExecutionStatus.ProcessStartedWithMaintenanceWarnings,
            null,
            ["cleanup failed"]);

        presenter.ShowMaintenanceWarning(result, new LaunchFeedbackContext("The application", LaunchFeedbackSource.InteractiveUi)
        {
            Owner = owner,
            SummaryName = "Browser"
        });

        messageBox.Verify(
            m => m.Show(
                owner,
                It.Is<string>(text => text.Contains("cleanup failed", StringComparison.Ordinal)),
                "RunFence",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button1),
            Times.Once);
        trayBalloon.Verify(t => t.ShowWarning(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void ShowMaintenanceWarning_WhenInteractiveSourceButOwnerHidden_DoesNotUseHiddenOwner()
    {
        var messageBox = new Mock<IAccountMessageBoxService>();
        var trayBalloon = new Mock<ITrayBalloonService>();
        var log = new Mock<ILoggingService>();
        using var owner = new Panel
        {
            Visible = false
        };
        var handle = owner.Handle;

        var presenter = CreatePresenter(
            log.Object,
            messageBox.Object,
            trayBalloon.Object);

        var result = new LaunchExecutionResult(
            LaunchExecutionStatus.ProcessStartedWithMaintenanceWarnings,
            null,
            ["cleanup failed"]);

        presenter.ShowMaintenanceWarning(result, new LaunchFeedbackContext("The application", LaunchFeedbackSource.InteractiveUi)
        {
            Owner = owner,
            SummaryName = "Browser"
        });

        messageBox.Verify(
            m => m.Show(
                null,
                It.Is<string>(text => text.Contains("cleanup failed", StringComparison.Ordinal)),
                "RunFence",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button1),
            Times.Once);
        trayBalloon.Verify(t => t.ShowWarning(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void ShowLaunchFailure_WhenSilentIpc_RateLimitsTrayNotifications()
    {
        var messageBox = new Mock<IAccountMessageBoxService>();
        var trayBalloon = new Mock<ITrayBalloonService>();
        var log = new Mock<ILoggingService>();
        var clock = new ManualClock(new DateTime(2026, 5, 7, 0, 0, 0, DateTimeKind.Utc));
        var presenter = CreatePresenter(
            log.Object,
            messageBox.Object,
            trayBalloon.Object,
            clock);

        for (int i = 0; i < 6; i++)
        {
            presenter.ShowLaunchFailure(
                "Launch failed: boom",
                new InvalidOperationException("boom"),
                new LaunchFeedbackContext("The application", LaunchFeedbackSource.SilentIpc)
                {
                    SummaryName = "Browser"
                });
        }

        trayBalloon.Verify(t => t.ShowWarning("Launch failed: Browser"), Times.Exactly(5));

        clock.Advance(TimeSpan.FromMinutes(5));

        presenter.ShowLaunchFailure(
            "Launch failed: boom",
            new InvalidOperationException("boom"),
            new LaunchFeedbackContext("The application", LaunchFeedbackSource.SilentIpc)
            {
                SummaryName = "Browser"
            });

        trayBalloon.Verify(t => t.ShowWarning("Launch failed: Browser"), Times.Exactly(6));
        messageBox.Verify(m => m.Show(It.IsAny<IWin32Window?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<MessageBoxButtons>(), It.IsAny<MessageBoxIcon>(), It.IsAny<MessageBoxDefaultButton>()), Times.Never);
    }

    private static LaunchFeedbackPresenter CreatePresenter(
        ILoggingService log,
        IAccountMessageBoxService messageBoxService,
        ITrayBalloonService trayBalloonService,
        IClock? clock = null)
    {
        return new LaunchFeedbackPresenter(
            log,
            messageBoxService,
            new MockTrayWarningSink(trayBalloonService),
            clock ?? new SystemClock());
    }

    private sealed class ManualClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow { get; private set; } = utcNow;

        public void Advance(TimeSpan delta) => UtcNow = UtcNow.Add(delta);
    }

    private sealed class MockTrayWarningSink(ITrayBalloonService trayBalloonService) : ITrayWarningSink
    {
        public void ShowWarning(string text) => trayBalloonService.ShowWarning(text);
    }
}
