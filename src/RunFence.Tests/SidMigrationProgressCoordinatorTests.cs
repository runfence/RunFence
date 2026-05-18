using Moq;
using RunFence.Apps.UI;
using RunFence.Core;
using RunFence.Infrastructure;
using RunFence.SidMigration.UI;
using RunFence.Tests.Helpers;
using Xunit;

namespace RunFence.Tests;

public class SidMigrationProgressCoordinatorTests
{
    [Fact]
    public void RunGuardedAsync_AfterBackDrivenCancelError_NextPlainCancelDoesNotNavigateBack()
    {
        StaTestHelper.RunAsyncOnSta(async () =>
        {
            var clock = new ManualClock(DateTime.UtcNow);
            using var coordinator = new SidMigrationProgressCoordinator(
                Mock.Of<ILoggingService>(),
                Mock.Of<IMessageBoxService>(),
                new SidMigrationDiskApplyController(clock));
            var owner = new TestWindow();

            var errorStep = new TestProgressStepView();
            coordinator.BeginProgressStep(errorStep);
            Assert.True(coordinator.TryHandleSecondaryAction(currentStep: 4, canShowBack: true, owner));

            var backAfterCancelCount = 0;
            var cancelCount = 0;
            await coordinator.RunGuardedAsync(
                operation: () => throw new InvalidOperationException("boom"),
                errorLogPrefix: "scan",
                step: errorStep,
                onCompleted: () => Assert.Fail("Operation should not complete successfully."),
                onCancel: () => cancelCount++,
                onNavigateBackAfterCancel: () => backAfterCancelCount++);

            Assert.Equal(0, backAfterCancelCount);
            Assert.Equal(0, cancelCount);

            var plainCancelStep = new TestProgressStepView();
            coordinator.BeginProgressStep(plainCancelStep);

            await coordinator.RunGuardedAsync(
                operation: () => throw new OperationCanceledException(),
                errorLogPrefix: "scan",
                step: plainCancelStep,
                onCompleted: () => Assert.Fail("Operation should not complete successfully."),
                onCancel: () => cancelCount++,
                onNavigateBackAfterCancel: () => backAfterCancelCount++);

            Assert.Equal(0, backAfterCancelCount);
            Assert.Equal(1, cancelCount);
        });
    }

    private sealed class TestProgressStepView : UserControl, ISidMigrationProgressStepView
    {
        public Control View => this;

        public ProgressBar ProgressBar { get; } = new();

        public Label StatusLabel { get; } = new();

        public Button CancelButton { get; } = new();

        public void Configure(string statusText, int? maxValue, bool showCancelButton)
        {
            StatusLabel.Text = statusText;
            CancelButton.Visible = showCancelButton;
            ProgressBar.Style = maxValue.HasValue ? ProgressBarStyle.Continuous : ProgressBarStyle.Marquee;
            if (maxValue.HasValue)
                ProgressBar.Maximum = maxValue.Value;
        }
    }

    private sealed class TestWindow : IWin32Window
    {
        public IntPtr Handle => IntPtr.Zero;
    }

    private sealed class ManualClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow { get; set; } = utcNow;
    }
}
