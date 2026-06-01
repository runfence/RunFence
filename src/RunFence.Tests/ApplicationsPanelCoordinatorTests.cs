using Moq;
using RunFence.Account;
using RunFence.Apps.UI;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Launch;
using RunFence.Persistence;
using RunFence.RunAs;
using RunFence.Tests.Helpers;
using Xunit;

namespace RunFence.Tests;

public class ApplicationsPanelCoordinatorTests
{
    [Fact]
    public void CommandCoordinator_SecondInitialize_ThrowsAndDoesNotReplaceFirstView()
    {
        var dialogFactory = new Mock<IOpenFileDialogAdapterFactory>(MockBehavior.Strict);
        var selectedApp = new AppEntry { Name = "Selected App" };
        var appLauncher = new Mock<IAppEntryLauncher>();
        appLauncher
            .Setup(l => l.Launch(selectedApp, null, null, It.IsAny<Func<string, string, bool>>(), null))
            .Returns(new LaunchExecutionResult(LaunchExecutionStatus.ProcessStarted, null));
        var launchHandler = new ApplicationsPanelLaunchHandler(
            appLauncher.Object,
            Mock.Of<ISidNameCacheService>(),
            Mock.Of<ILaunchFeedbackPresenter>(),
            Mock.Of<ILoggingService>(),
            Mock.Of<IMessageBoxService>(),
            Mock.Of<IRunAsFlowHandler>());
        var coordinator = new ApplicationsPanelCommandCoordinator(
            null!,
            launchHandler,
            dialogFactory.Object);

        var firstView = new Mock<IApplicationsPanelCommandView>();
        firstView.Setup(v => v.GetSelectedApp()).Returns(selectedApp);
        firstView.Setup(v => v.GetOwner()).Returns(new Control());
        var secondView = new Mock<IApplicationsPanelCommandView>();
        coordinator.Initialize(firstView.Object);

        var ex = Assert.Throws<InvalidOperationException>(() => coordinator.Initialize(secondView.Object));
        Assert.Equal("ApplicationsPanelCommandCoordinator is already initialized.", ex.Message);

        coordinator.HandleLaunchSelected();

        appLauncher.Verify(
            l => l.Launch(selectedApp, null, null, It.IsAny<Func<string, string, bool>>(), null),
            Times.Once);
        secondView.VerifyNoOtherCalls();
    }

    [Fact]
    public void RefreshCoordinator_SecondInitialize_ThrowsAndUsesOriginalViewOnly()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var appConfigService = new Mock<IAppConfigService>();
            appConfigService.SetupGet(service => service.HasLoadedConfigs).Returns(false);
            appConfigService.Setup(service => service.GetLoadedConfigPaths()).Returns([]);
            appConfigService.Setup(service => service.GetConfigPath(It.IsAny<string>())).Returns((string?)null);

            var grid = new DataGridView();
            grid.Columns.Add("AppName", "Name");
            var gridPopulator = new ApplicationsGridPopulator(
                Mock.Of<IIconService>(),
                appConfigService.Object,
                Mock.Of<ISidNameCacheService>());
            var state = new TestApplicationsPanelState(new AppDatabase(), new CredentialStore());
            gridPopulator.Initialize(grid, state, (items, _) => items.OrderBy(app => app.Name, StringComparer.OrdinalIgnoreCase));

            var dragDropHandler = new AppGridDragDropHandler(appConfigService.Object);
            dragDropHandler.Initialize(grid, state, _ => { });

            var saveHelper = new ApplicationsPanelSaveHelper(
                appConfigService.Object,
                Mock.Of<ISessionProvider>());
            var coordinator = new ApplicationsPanelRefreshCoordinator(
                gridPopulator,
                dragDropHandler,
                saveHelper);

            var firstView = new RecordingRefreshView();
            var secondView = new RecordingRefreshView();
            coordinator.Initialize(firstView);

            var ex = Assert.Throws<InvalidOperationException>(() => coordinator.Initialize(secondView));
            Assert.Equal("ApplicationsPanelRefreshCoordinator is already initialized.", ex.Message);

            coordinator.RefreshAfterInMemoryMutation(appId: "app-1");
            Assert.Equal(["app-1"], firstView.SelectedAppIds);
            Assert.Equal(1, firstView.DataChangedCount);
            Assert.Empty(secondView.SelectedAppIds);
            Assert.Equal(0, secondView.DataChangedCount);
        });
    }

    [Fact]
    public void RefreshCoordinator_SeparateInstances_KeepSelectionsAndNotificationsIsolated()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var appConfigService = new Mock<IAppConfigService>();
            appConfigService.SetupGet(service => service.HasLoadedConfigs).Returns(false);
            appConfigService.Setup(service => service.GetLoadedConfigPaths()).Returns([]);
            appConfigService.Setup(service => service.GetConfigPath(It.IsAny<string>())).Returns((string?)null);

            var gridOne = new DataGridView();
            gridOne.Columns.Add("AppName", "Name");
            var gridTwo = new DataGridView();
            gridTwo.Columns.Add("AppName", "Name");

            var stateOne = new TestApplicationsPanelState(new AppDatabase(), new CredentialStore());
            var stateTwo = new TestApplicationsPanelState(new AppDatabase(), new CredentialStore());
            var gridPopulatorOne = new ApplicationsGridPopulator(
                Mock.Of<IIconService>(),
                appConfigService.Object,
                Mock.Of<ISidNameCacheService>());
            gridPopulatorOne.Initialize(gridOne, stateOne, (items, _) => items.OrderBy(app => app.Name, StringComparer.OrdinalIgnoreCase));

            var gridPopulatorTwo = new ApplicationsGridPopulator(
                Mock.Of<IIconService>(),
                appConfigService.Object,
                Mock.Of<ISidNameCacheService>());
            gridPopulatorTwo.Initialize(gridTwo, stateTwo, (items, _) => items.OrderBy(app => app.Name, StringComparer.OrdinalIgnoreCase));

            var dragDropHandlerOne = new AppGridDragDropHandler(appConfigService.Object);
            dragDropHandlerOne.Initialize(gridOne, stateOne, _ => { });
            var dragDropHandlerTwo = new AppGridDragDropHandler(appConfigService.Object);
            dragDropHandlerTwo.Initialize(gridTwo, stateTwo, _ => { });

            var saveHelper = new ApplicationsPanelSaveHelper(
                appConfigService.Object,
                Mock.Of<ISessionProvider>());
            var firstCoordinator = new ApplicationsPanelRefreshCoordinator(
                gridPopulatorOne,
                dragDropHandlerOne,
                saveHelper);
            var secondCoordinator = new ApplicationsPanelRefreshCoordinator(
                gridPopulatorTwo,
                dragDropHandlerTwo,
                saveHelper);

            var firstView = new RecordingRefreshView();
            var secondView = new RecordingRefreshView();
            firstCoordinator.Initialize(firstView);
            secondCoordinator.Initialize(secondView);

            firstCoordinator.RefreshAfterInMemoryMutation(appId: "app-1");
            secondCoordinator.RefreshAfterInMemoryMutation(fallbackIndex: 2);

            Assert.Equal(["app-1"], firstView.SelectedAppIds);
            Assert.Equal(1, firstView.DataChangedCount);
            Assert.Empty(firstView.SelectedRowIndexes);

            Assert.Empty(secondView.SelectedAppIds);
            Assert.Equal([2], secondView.SelectedRowIndexes);
            Assert.Equal(1, secondView.DataChangedCount);
        });
    }

    private sealed class RecordingRefreshView : IApplicationsPanelRefreshView
    {
        public List<string?> SelectedAppIds { get; } = [];
        public List<int> SelectedRowIndexes { get; } = [];
        public int DataChangedCount { get; private set; }

        public void SetIsRefreshing(bool isRefreshing) { }
        public void ReapplyGlyphIfActive() { }
        public void UpdateButtonState() { }
        public void SelectAppById(string? appId) => SelectedAppIds.Add(appId);
        public void SelectRowByIndex(int rowIndex) => SelectedRowIndexes.Add(rowIndex);
        public void SelectFirstRow() { }
        public void PublishDataChanged() => DataChangedCount++;
    }

    private sealed record TestApplicationsPanelState(
        AppDatabase Database,
        CredentialStore CredentialStore) : IApplicationsPanelState
    {
        public bool IsSortActive => false;
        public int SortColumnIndex => 1;
    }
}
