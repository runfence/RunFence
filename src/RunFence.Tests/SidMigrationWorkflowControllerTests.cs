using Moq;
using System.ComponentModel;
using RunFence.Apps.UI;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.SidMigration;
using RunFence.SidMigration.UI;
using RunFence.Tests.Helpers;
using Xunit;

namespace RunFence.Tests;

public class SidMigrationWorkflowControllerTests
{
    [Fact]
    public void HandleSecondary_OnStep3_ReturnsToStep1()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var context = CreateContext();
            context.Controller.Initialize();
            context.StepFactory.LastPathStep!.SelectedPaths = ["C:\\Data"];
            context.StepFactory.LastPathStep.RaiseSkipRequested();
            context.Controller.HandleViewShown(TestWindow.Instance);

            Assert.Equal("Start Scan", context.Controller.CurrentView.NextText);
            Assert.Equal("Back", context.Controller.CurrentView.SecondaryText);
            Assert.False(context.Controller.CurrentView.SecondaryActsAsCancel);

            context.Controller.HandleSecondary(TestWindow.Instance);

            Assert.Equal("Step 1: Select Paths to Scan", context.Controller.CurrentView.Title);
        });
    }

    [Fact]
    public void HandleSecondary_OnStep3AfterDiscovery_RequestsCloseInsteadOfReturningToStep1()
    {
        StaTestHelper.RunAsyncOnSta(async () =>
        {
            using var context = CreateContext();
            var closeRequested = false;
            context.Controller.CloseRequested += () => closeRequested = true;

            context.Controller.Initialize();
            context.StepFactory.LastPathStep!.SelectedPaths = ["C:\\Data"];
            context.Controller.HandleNext(TestWindow.Instance);
            context.Controller.HandleViewShown(TestWindow.Instance);
            await context.WaitForTitleAsync("Step 3: Review SID Mappings");
            context.Controller.HandleViewShown(TestWindow.Instance);
            await context.WaitForViewReadyAsync();

            context.Controller.HandleSecondary(TestWindow.Instance);

            Assert.True(closeRequested);
            Assert.Equal("Step 3: Review SID Mappings", context.Controller.CurrentView.Title);
        });
    }

    [Fact]
    public void HandleSecondary_OnStep5_ReturnsToStep3()
    {
        StaTestHelper.RunAsyncOnSta(async () =>
        {
            using var context = CreateContext();
            context.StepFactory.MappingSelections = new MappingSelectionResult(
                [new SidMigrationMapping("S-1-old", "old-user", "S-1-new")],
                []);
            context.ScanResults = [new SidMigrationMatch { Path = @"C:\Data\File.txt", IsDirectory = false, MatchType = SidMigrationMatchType.Ace }];

            context.Controller.Initialize();
            context.StepFactory.LastPathStep!.SelectedPaths = ["C:\\Data"];
            context.Controller.HandleNext(TestWindow.Instance);
            context.Controller.HandleViewShown(TestWindow.Instance);
            await context.WaitForTitleAsync("Step 3: Review SID Mappings");
            context.Controller.HandleViewShown(TestWindow.Instance);
            await context.WaitForViewReadyAsync();

            context.Controller.HandleNext(TestWindow.Instance);
            Assert.Equal("Step 4: Scanning Disk", context.Controller.CurrentView.Title);

            context.Controller.HandleViewShown(TestWindow.Instance);
            await context.WaitForTitleAsync("Step 5: Preview Changes");

            context.Controller.HandleSecondary(TestWindow.Instance);

            Assert.Equal("Step 3: Review SID Mappings", context.Controller.CurrentView.Title);
        });
    }

    [Fact]
    public void HandleNext_ProgressesThroughWorkflowSteps()
    {
        StaTestHelper.RunAsyncOnSta(async () =>
        {
            using var context = CreateContext();
            context.StepFactory.MappingSelections = new MappingSelectionResult(
                [new SidMigrationMapping("S-1-old", "S-1-new", "old-user")],
                []);
            context.ScanResults = [new SidMigrationMatch { Path = @"C:\Data\File.txt", IsDirectory = false, MatchType = SidMigrationMatchType.Ace }];

            context.Controller.Initialize();
            Assert.Equal("Step 1: Select Paths to Scan", context.Controller.CurrentView.Title);

            context.StepFactory.LastPathStep!.SelectedPaths = ["C:\\Data"];
            context.Controller.HandleNext(TestWindow.Instance);
            Assert.Equal("Step 2: Discovering Orphaned SIDs", context.Controller.CurrentView.Title);

            context.Controller.HandleViewShown(TestWindow.Instance);
            await context.WaitForTitleAsync("Step 3: Review SID Mappings");
            context.Controller.HandleViewShown(TestWindow.Instance);
            await context.WaitForViewReadyAsync();

            context.Controller.HandleNext(TestWindow.Instance);
            Assert.Equal("Step 4: Scanning Disk", context.Controller.CurrentView.Title);

            context.Controller.HandleViewShown(TestWindow.Instance);
            await context.WaitForTitleAsync("Step 5: Preview Changes");

            context.Controller.HandleNext(TestWindow.Instance);
            Assert.Equal("Step 6: Applying Changes", context.Controller.CurrentView.Title);

            context.Controller.HandleViewShown(TestWindow.Instance);
            await context.WaitForViewStateAsync(view => view.Title == "Step 6: Applying Changes" && view.NextEnabled);

            context.Controller.HandleNext(TestWindow.Instance);
            Assert.Equal("Step 7: In-App Data Migration", context.Controller.CurrentView.Title);
        });
    }

    [Fact]
    public void DiscoveryCompletion_WithUnresolvedSids_ShowsWarningAndAdvancesToStep3()
    {
        StaTestHelper.RunAsyncOnSta(async () =>
        {
            using var context = CreateContext();
            context.DiscoveredSids =
            [
                new OrphanedSid { Sid = "S-1-unresolved", Classification = OrphanedSidClassification.Unresolved }
            ];

            context.Controller.Initialize();
            context.StepFactory.LastPathStep!.SelectedPaths = ["C:\\Data"];
            context.Controller.HandleNext(TestWindow.Instance);

            Assert.Equal("Step 2: Discovering Orphaned SIDs", context.Controller.CurrentView.Title);

            context.Controller.HandleViewShown(TestWindow.Instance);
            await context.WaitForTitleAsync("Step 3: Review SID Mappings");

            context.MessageBoxService.Verify(service => service.Show(
                TestWindow.Instance,
                It.Is<string>(text => text.Contains("S-1-unresolved", StringComparison.Ordinal)),
                "Discovery Finished",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning), Times.Once);
        });
    }

    [Fact]
    public void DiscoveryCompletion_OnStep3_ShowsExecuteAndCancel()
    {
        StaTestHelper.RunAsyncOnSta(async () =>
        {
            using var context = CreateContext();

            context.Controller.Initialize();
            context.StepFactory.LastPathStep!.SelectedPaths = ["C:\\Data"];
            context.Controller.HandleNext(TestWindow.Instance);

            context.Controller.HandleViewShown(TestWindow.Instance);
            await context.WaitForTitleAsync("Step 3: Review SID Mappings");
            context.Controller.HandleViewShown(TestWindow.Instance);
            await context.WaitForViewReadyAsync();

            Assert.Equal("Execute", context.Controller.CurrentView.NextText);
            Assert.Equal("Cancel", context.Controller.CurrentView.SecondaryText);
            Assert.True(context.Controller.CurrentView.SecondaryActsAsCancel);
        });
    }

    [Fact]
    public void DiskScanCompletion_WithOwnerDeleteSid_AdvancesToStep5()
    {
        StaTestHelper.RunAsyncOnSta(async () =>
        {
            using var context = CreateContext();
            context.ScanResults =
            [
                new SidMigrationMatch
                {
                    Path = @"C:\Data\File.txt",
                    IsDirectory = false,
                    MatchType = SidMigrationMatchType.Owner,
                    OwnerOldSid = "S-1-old"
                }
            ];
            context.StepFactory.MappingSelections = new MappingSelectionResult([], ["S-1-old"]);

            context.Controller.Initialize();
            context.StepFactory.LastPathStep!.SelectedPaths = ["C:\\Data"];
            context.Controller.HandleNext(TestWindow.Instance);
            context.Controller.HandleViewShown(TestWindow.Instance);
            await context.WaitForTitleAsync("Step 3: Review SID Mappings");
            context.Controller.HandleViewShown(TestWindow.Instance);
            await context.WaitForViewReadyAsync();

            context.Controller.HandleNext(TestWindow.Instance);
            context.Controller.HandleViewShown(TestWindow.Instance);
            await context.WaitForTitleAsync("Step 5: Preview Changes");
        });
    }

    [Fact]
    public void HandleFormClosing_OnStep6_BlocksCloseUntilStep7()
    {
        StaTestHelper.RunAsyncOnSta(async () =>
        {
            using var context = CreateContext();
            context.StepFactory.MappingSelections = new MappingSelectionResult(
                [new SidMigrationMapping("S-1-old", "old-user", "S-1-new")],
                []);
            context.ScanResults = [new SidMigrationMatch { Path = @"C:\Data\File.txt", IsDirectory = false, MatchType = SidMigrationMatchType.Ace }];

            context.Controller.Initialize();
            context.StepFactory.LastPathStep!.SelectedPaths = ["C:\\Data"];
            context.Controller.HandleNext(TestWindow.Instance);
            context.Controller.HandleViewShown(TestWindow.Instance);
            await context.WaitForTitleAsync("Step 3: Review SID Mappings");
            context.Controller.HandleViewShown(TestWindow.Instance);
            await context.WaitForViewReadyAsync();
            context.Controller.HandleNext(TestWindow.Instance);
            context.Controller.HandleViewShown(TestWindow.Instance);
            await context.WaitForTitleAsync("Step 5: Preview Changes");

            context.Controller.HandleNext(TestWindow.Instance);

            var closingArgs = new FormClosingEventArgs(CloseReason.UserClosing, false);
            context.Controller.HandleFormClosing(TestWindow.Instance, closingArgs);

            Assert.True(closingArgs.Cancel);
            context.MessageBoxService.Verify(service => service.Show(
                TestWindow.Instance,
                It.Is<string>(text => text.Contains("Step 7 (In-App Migration) must be completed before closing.", StringComparison.Ordinal)),
                "Cannot Close",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning), Times.Once);
        });
    }

    [Fact]
    public void GetCloseDialogResult_AfterInAppMigrationApplied_ReturnsOk()
    {
        StaTestHelper.RunAsyncOnSta(async () =>
        {
            using var context = CreateContext();
            context.StepFactory.MappingSelections = new MappingSelectionResult(
                [new SidMigrationMapping("S-1-old", "old-user", "S-1-new")],
                []);
            context.ScanResults = [new SidMigrationMatch { Path = @"C:\Data\File.txt", IsDirectory = false, MatchType = SidMigrationMatchType.Ace }];

            context.Controller.Initialize();
            context.StepFactory.LastPathStep!.SelectedPaths = ["C:\\Data"];
            context.Controller.HandleNext(TestWindow.Instance);
            context.Controller.HandleViewShown(TestWindow.Instance);
            await context.WaitForTitleAsync("Step 3: Review SID Mappings");
            context.Controller.HandleViewShown(TestWindow.Instance);
            await context.WaitForViewReadyAsync();
            context.Controller.HandleNext(TestWindow.Instance);
            context.Controller.HandleViewShown(TestWindow.Instance);
            await context.WaitForTitleAsync("Step 5: Preview Changes");
            context.Controller.HandleNext(TestWindow.Instance);
            context.Controller.HandleViewShown(TestWindow.Instance);
            await context.WaitForViewStateAsync(view => view.Title == "Step 6: Applying Changes" && view.NextEnabled);

            context.Controller.HandleNext(TestWindow.Instance);
            Assert.Equal("Step 7: In-App Data Migration", context.Controller.CurrentView.Title);

            context.StepFactory.LastInAppStep!.RaiseMigrationApplied();

            Assert.Equal(DialogResult.OK, context.Controller.GetCloseDialogResult());
        });
    }

    [Fact]
    public void HandleEscape_OnStep3WithBackVisible_IsNotHandled()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var context = CreateContext();
            context.Controller.Initialize();
            context.StepFactory.LastPathStep!.SelectedPaths = ["C:\\Data"];
            context.StepFactory.LastPathStep.RaiseSkipRequested();
            context.Controller.HandleViewShown(TestWindow.Instance);

            var handled = context.Controller.HandleEscape(TestWindow.Instance);

            Assert.False(handled);
            Assert.Equal("Step 3: Review SID Mappings", context.Controller.CurrentView.Title);
        });
    }

    private static TestContext CreateContext()
    {
        var session = new SessionContext
{
            Database = new AppDatabase
            {
                SidNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            },
            CredentialStore = new CredentialStore
            {
                Credentials =
                [
                    new CredentialEntry { Sid = "S-1-old" }
                ]
            },
        }.WithPinDerivedKeyTakingOwnership(TestSecretFactory.Create(32));

        var sidMigrationService = new Mock<ISidMigrationService>();
        var messageBoxService = new Mock<IMessageBoxService>();
        var stepFactory = new TestSidMigrationStepFactory(session);
        var state = new SidMigrationWorkflowState();
        var clock = new ManualClock(DateTime.UtcNow);

        var context = new TestContext(session, messageBoxService, sidMigrationService, stepFactory);

        sidMigrationService.Setup(service => service.DiscoverOrphanedSidsAsync(
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<IProgress<(long scanned, long sidsFound)>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => context.DiscoveredSids);
        sidMigrationService.Setup(service => service.ScanAsync(
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<IReadOnlyList<SidMigrationMapping>>(),
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<IProgress<(long scanned, long found)>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => context.ScanResults);
        sidMigrationService.Setup(service => service.ApplyAsync(
                It.IsAny<IReadOnlyList<SidMigrationMatch>>(),
                It.IsAny<IReadOnlyList<SidMigrationMapping>>(),
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<IProgress<MigrationProgress>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((1L, 0L));

        context.Controller = new SidMigrationWorkflowController(
            session,
            sidMigrationService.Object,
            messageBoxService.Object,
            state,
            new SidMigrationProgressCoordinator(
                Mock.Of<ILoggingService>(),
                messageBoxService.Object,
                new SidMigrationDiskApplyController(clock)),
            stepFactory,
            new SidMigrationDiscoveryStepController(),
            new SidMigrationDiskScanStepController());

        return context;
    }

    private sealed class TestContext(
        SessionContext session,
        Mock<IMessageBoxService> messageBoxService,
        Mock<ISidMigrationService> sidMigrationService,
        TestSidMigrationStepFactory stepFactory) : IDisposable
    {
        public SidMigrationWorkflowController Controller { get; set; } = null!;

        public List<OrphanedSid> DiscoveredSids { get; set; } = [];

        public List<SidMigrationMatch> ScanResults { get; set; } = [];

        public Mock<IMessageBoxService> MessageBoxService { get; } = messageBoxService;

        public Mock<ISidMigrationService> SidMigrationService { get; } = sidMigrationService;

        public TestSidMigrationStepFactory StepFactory { get; } = stepFactory;

        public Task WaitForTitleAsync(string title)
        {
            return WaitForViewStateAsync(view => view.Title == title);
        }

        public Task WaitForViewReadyAsync()
        {
            return WaitForViewStateAsync(view => view.NextEnabled && view.SecondaryEnabled);
        }

        public Task WaitForViewStateAsync(Func<SidMigrationDialogViewState, bool> predicate)
        {
            StaTestHelper.PumpUntil(() => predicate(Controller.CurrentView));
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            Controller.Dispose();
            session.Dispose();
        }
    }

    private sealed class TestSidMigrationStepFactory(SessionContext session) : ISidMigrationStepFactory
    {
        public MappingSelectionResult MappingSelections { get; set; } = new([], []);

        public TestPathStepView? LastPathStep { get; private set; }

        public TestMappingStepView? LastMappingStep { get; private set; }

        public TestProgressStepView? LastDiscoveryProgressStep { get; private set; }

        public TestProgressStepView? LastDiskScanProgressStep { get; private set; }

        public TestDiskApplyStepView? LastDiskApplyStep { get; private set; }

        public TestInAppStepView? LastInAppStep { get; private set; }

        public ISidMigrationPathStepView CreatePathStep(bool showSkipButton)
        {
            LastPathStep = new TestPathStepView();
            return LastPathStep;
        }

        public ISidMigrationProgressStepView CreateDiscoveryProgressStep()
        {
            LastDiscoveryProgressStep = new TestProgressStepView();
            return LastDiscoveryProgressStep;
        }

        public ISidMigrationMappingStepView CreateMappingStep(IReadOnlyList<OrphanedSid> orphanedSids)
        {
            LastMappingStep = new TestMappingStepView(MappingSelections);
            return LastMappingStep;
        }

        public ISidMigrationProgressStepView CreateDiskScanProgressStep()
        {
            LastDiskScanProgressStep = new TestProgressStepView();
            return LastDiskScanProgressStep;
        }

        public ISidMigrationStepView CreatePreviewStep(IReadOnlyList<SidMigrationMatch> scanResults)
        {
            return new TestStepView();
        }

        public ISidMigrationDiskApplyStepView CreateDiskApplyProgressStep()
        {
            LastDiskApplyStep = new TestDiskApplyStepView();
            return LastDiskApplyStep;
        }

        public ISidMigrationInAppStepView CreateInAppStep(
            IReadOnlyList<SidMigrationMapping> filteredMappings,
            IReadOnlyList<string> filteredDeletes,
            IEnumerable<string> unresolvedSids)
        {
            LastInAppStep = new TestInAppStepView(session, filteredMappings, filteredDeletes);
            return LastInAppStep;
        }
    }

    private sealed record MappingSelectionResult(List<SidMigrationMapping> Mappings, List<string> DeleteSids);

    private class TestStepView : UserControl, ISidMigrationStepView
    {
        public Control View => this;
    }

    private sealed class TestPathStepView : TestStepView, ISidMigrationPathStepView
    {
        public event EventHandler? SkipRequested;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public List<string> SelectedPaths { get; set; } = [];

        public List<(string path, bool isChecked)>? SavedState { get; private set; }

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

    private sealed class TestMappingStepView(MappingSelectionResult selectionResult) : TestStepView, ISidMigrationMappingStepView
    {
        public void BeginAsync(Action onReady, Action onFailed)
        {
            onReady();
        }

        public bool TryCollectSelections(out List<SidMigrationMapping> mappings, out List<string> deleteSids)
        {
            mappings = selectionResult.Mappings.ToList();
            deleteSids = selectionResult.DeleteSids.ToList();
            return true;
        }
    }

    private class TestProgressStepView : TestStepView, ISidMigrationProgressStepView
    {
        public ProgressBar ProgressBar { get; } = new();

        public Label StatusLabel { get; } = new();

        public Button CancelButton { get; } = new();

        public void Configure(string statusText, int? maxValue, bool showCancelButton)
        {
            StatusLabel.Text = statusText;
            CancelButton.Visible = showCancelButton;
            if (maxValue.HasValue)
            {
                ProgressBar.Style = ProgressBarStyle.Continuous;
                ProgressBar.Maximum = maxValue.Value;
            }
            else
            {
                ProgressBar.Style = ProgressBarStyle.Marquee;
            }
        }
    }

    private sealed class TestDiskApplyStepView : TestProgressStepView, ISidMigrationDiskApplyStepView
    {
        public string CurrentPath { get; private set; } = string.Empty;

        public void SetCurrentPath(string currentPath)
        {
            CurrentPath = currentPath;
        }
    }

    private sealed class TestInAppStepView(
        SessionContext session,
        IReadOnlyList<SidMigrationMapping> filteredMappings,
        IReadOnlyList<string> filteredDeletes) : TestStepView, ISidMigrationInAppStepView
    {
        public event EventHandler? MigrationApplied;

        public SessionContext Session { get; } = session;

        public IReadOnlyList<SidMigrationMapping> FilteredMappings { get; } = filteredMappings;

        public IReadOnlyList<string> FilteredDeletes { get; } = filteredDeletes;

        public void RaiseMigrationApplied()
        {
            MigrationApplied?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class TestWindow : IWin32Window
    {
        public static TestWindow Instance { get; } = new();

        public IntPtr Handle => IntPtr.Zero;
    }

    private sealed class ManualClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow { get; set; } = utcNow;
    }
}
