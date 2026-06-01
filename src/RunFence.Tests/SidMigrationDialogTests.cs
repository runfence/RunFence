using Moq;
using System.ComponentModel;
using RunFence.Account;
using RunFence.Apps.UI;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.SidMigration;
using RunFence.SidMigration.UI;
using RunFence.SidMigration.UI.Forms;
using RunFence.Tests.Helpers;
using Xunit;

namespace RunFence.Tests;

public class SidMigrationDialogTests
{
    [Fact]
    public void Escape_OnFirstStep_IsHandledWhenCancelIsVisible()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var workflow = CreateWorkflowController();
            using var session = workflow.Session;
            using var controller = workflow.Controller;
            using var dialog = new TestSidMigrationDialog(controller);
            StaTestHelper.CreateControlTree(dialog);

            Assert.True(dialog.TriggerEscape());
        });
    }

    [Fact]
    public void WorkflowViewUpdate_WithSameStepControl_DoesNotReplaceHostedControl()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var stepFactory = new SameControlUpdateStepFactory();
            using var session = new SessionContext
{
                Database = new AppDatabase(),
                CredentialStore = new CredentialStore(),
            }.WithPinDerivedKeyTakingOwnership(TestSecretFactory.Create(32));
            using var controller = new SidMigrationWorkflowController(
                session,
                Mock.Of<ISidMigrationService>(),
                Mock.Of<IMessageBoxService>(),
                new SidMigrationWorkflowState(),
                new SidMigrationProgressCoordinator(
                    Mock.Of<ILoggingService>(),
                    Mock.Of<IMessageBoxService>(),
                    new SidMigrationDiskApplyController(new ManualClock(DateTime.UtcNow))),
                stepFactory,
                new SidMigrationDiscoveryStepController(),
                new SidMigrationDiskScanStepController());
            using var dialog = new SidMigrationDialog(controller);
            StaTestHelper.CreateControlTree(dialog);

            stepFactory.LastPathStep!.SelectedPaths = [@"C:\Data"];
            stepFactory.LastPathStep.RaiseSkipRequested();

            var hostedControl = FindControls<Panel>(dialog)
                .Single(panel => panel.Controls.Count == 1 && panel.Dock == DockStyle.Fill)
                .Controls[0];

            Assert.Same(stepFactory.LastMappingStep, hostedControl);
            Assert.False(hostedControl.IsDisposed);
        });
    }

    private static (SidMigrationWorkflowController Controller, SessionContext Session) CreateWorkflowController()
    {
        var session = new SessionContext
{
            Database = new AppDatabase { SidNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) },
            CredentialStore = new CredentialStore(),
        }.WithPinDerivedKeyTakingOwnership(TestSecretFactory.Create(32));

        var sidMigrationService = new Mock<ISidMigrationService>();
        sidMigrationService.Setup(service => service.DiscoverOrphanedSidsAsync(
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<IProgress<(long scanned, long sidsFound)>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        sidMigrationService.Setup(service => service.ScanAsync(
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<IReadOnlyList<SidMigrationMapping>>(),
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<IProgress<(long scanned, long found)>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        sidMigrationService.Setup(service => service.ApplyAsync(
                It.IsAny<IReadOnlyList<SidMigrationMatch>>(),
                It.IsAny<IReadOnlyList<SidMigrationMapping>>(),
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<IProgress<MigrationProgress>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((0L, 0L));

        return (
            new SidMigrationWorkflowController(
                session,
                sidMigrationService.Object,
                Mock.Of<IMessageBoxService>(),
                new SidMigrationWorkflowState(),
                new SidMigrationProgressCoordinator(
                    Mock.Of<ILoggingService>(),
                    Mock.Of<IMessageBoxService>(),
                    new SidMigrationDiskApplyController(new ManualClock(DateTime.UtcNow))),
                new SidMigrationStepFactory(
                    session,
                    sidMigrationService.Object,
                    null!,
                    Mock.Of<ILocalUserProvider>(),
                    Mock.Of<ILoggingService>(),
                    Mock.Of<IProfilePathResolver>(),
                    Mock.Of<ISidNameCacheService>(),
                    Mock.Of<IMessageBoxService>()),
                new SidMigrationDiscoveryStepController(),
                new SidMigrationDiskScanStepController()),
            session);
    }

    private static IEnumerable<T> FindControls<T>(Control root) where T : Control
    {
        foreach (Control child in root.Controls)
        {
            if (child is T match)
                yield return match;

            foreach (var nested in FindControls<T>(child))
                yield return nested;
        }
    }

    private sealed class TestSidMigrationDialog(SidMigrationWorkflowController workflowController) : SidMigrationDialog(workflowController)
    {
        public bool TriggerEscape()
        {
            var message = new Message();
            return ProcessCmdKey(ref message, Keys.Escape);
        }
    }

    private sealed class SameControlUpdateStepFactory : ISidMigrationStepFactory
    {
        public TestPathStepView? LastPathStep { get; private set; }

        public TestMappingStepView? LastMappingStep { get; private set; }

        public ISidMigrationPathStepView CreatePathStep(bool showSkipButton)
        {
            LastPathStep = new TestPathStepView();
            return LastPathStep;
        }

        public ISidMigrationProgressStepView CreateDiscoveryProgressStep() => throw new NotSupportedException();

        public ISidMigrationMappingStepView CreateMappingStep(IReadOnlyList<OrphanedSid> orphanedSids)
        {
            LastMappingStep = new TestMappingStepView();
            return LastMappingStep;
        }

        public ISidMigrationProgressStepView CreateDiskScanProgressStep() => throw new NotSupportedException();

        public ISidMigrationStepView CreatePreviewStep(IReadOnlyList<SidMigrationMatch> scanResults) => throw new NotSupportedException();

        public ISidMigrationDiskApplyStepView CreateDiskApplyProgressStep() => throw new NotSupportedException();

        public ISidMigrationInAppStepView CreateInAppStep(
            IReadOnlyList<SidMigrationMapping> filteredMappings,
            IReadOnlyList<string> filteredDeletes,
            IEnumerable<string> unresolvedSids) => throw new NotSupportedException();
    }

    private sealed class TestPathStepView : UserControl, ISidMigrationPathStepView
    {
        public event EventHandler? SkipRequested;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public List<string> SelectedPaths { get; set; } = [];

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public List<(string path, bool isChecked)>? SavedState { get; private set; }

        public Control View => this;

        public void RestoreState(List<(string path, bool isChecked)> savedState)
        {
            SavedState = savedState.ToList();
        }

        public List<string> CollectSelectedPaths()
        {
            SavedState = SelectedPaths.Select(path => (path, true)).ToList();
            return SelectedPaths.ToList();
        }

        public void RaiseSkipRequested()
        {
            SkipRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class TestMappingStepView : UserControl, ISidMigrationMappingStepView
    {
        public Control View => this;

        public void BeginAsync(Action onReady, Action onFailed)
        {
            onReady();
        }

        public bool TryCollectSelections(out List<SidMigrationMapping> mappings, out List<string> deleteSids)
        {
            mappings = [];
            deleteSids = [];
            return true;
        }
    }

    private sealed class ManualClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow { get; set; } = utcNow;
    }
}
