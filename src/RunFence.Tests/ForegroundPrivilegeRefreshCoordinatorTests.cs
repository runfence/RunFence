using System.Drawing;
using Moq;
using RunFence.Core;
using RunFence.ForegroundMarker;
using RunFence.Infrastructure;
using Xunit;

namespace RunFence.Tests;

public sealed class ForegroundPrivilegeRefreshCoordinatorTests
{
    [Fact]
    public void ApplyClassificationResult_PublishesActiveStateWithMarkerColor()
    {
        var coordinator = CreateCoordinator(new FakeMarkerWindow(), new FakeBoundsReader(), out var request);
        var result = ForegroundPrivilegeClassificationResult.Visible(request, ForegroundPrivilegeMarkerKind.Isolated)
            with
            {
                Metadata = new ForegroundPrivilegeMarkerMetadata("chrome.exe", "S-1-5-21-test")
            };

        coordinator.ApplyClassificationResult(result);

        Assert.True(coordinator.CurrentState.IsActive);
        Assert.Equal(ForegroundPrivilegeMarkerKind.Isolated, coordinator.CurrentState.Kind);
        Assert.Equal(ForegroundPrivilegeMarkerPalette.Isolated, coordinator.CurrentState.Color);
        Assert.Equal("chrome.exe", coordinator.CurrentState.Metadata?.ProcessName);
    }

    [Fact]
    public void ApplyClassificationResult_ShowsMarkerDirectlyAboveTrackedWindow()
    {
        var markerWindow = new FakeMarkerWindow();
        var coordinator = CreateCoordinator(markerWindow, new FakeBoundsReader(), out var request);

        coordinator.ApplyClassificationResult(
            ForegroundPrivilegeClassificationResult.Visible(request, ForegroundPrivilegeMarkerKind.Basic));

        Assert.Equal((IntPtr)100, markerWindow.LastTargetWindow);
    }

    [Fact]
    public void ApplyClassificationResult_VisibleNullMetadata_UsesFallbackMetadata()
    {
        var coordinator = CreateCoordinator(new FakeMarkerWindow(), new FakeBoundsReader(), out var request);

        coordinator.ApplyClassificationResult(
            ForegroundPrivilegeClassificationResult.Visible(request, ForegroundPrivilegeMarkerKind.Basic));

        Assert.True(coordinator.CurrentState.IsActive);
        Assert.Equal("PID 200", coordinator.CurrentState.Metadata?.ProcessName);
        Assert.Null(coordinator.CurrentState.Metadata?.AccountSid);
    }

    [Fact]
    public void ApplyClassificationResult_HiddenHighIlWithoutMetadata_UsesFallbackTooltipOnlyState()
    {
        var coordinator = CreateCoordinator(new FakeMarkerWindow(), new FakeBoundsReader(), out var request);
        coordinator.ApplyClassificationResult(
            ForegroundPrivilegeClassificationResult.Visible(request, ForegroundPrivilegeMarkerKind.Basic));

        coordinator.ApplyClassificationResult(
            ForegroundPrivilegeClassificationResult.Hidden(
                request,
                tooltipMode: ForegroundPrivilegeTooltipMode.Elevated));

        Assert.False(coordinator.CurrentState.IsActive);
        Assert.Equal(ForegroundPrivilegeTooltipMode.Elevated, coordinator.CurrentState.TooltipMode);
        Assert.Equal("PID 200", coordinator.CurrentState.Metadata?.ProcessName);
        Assert.Null(coordinator.CurrentState.Metadata?.AccountSid);
    }

    [Fact]
    public void ApplyClassificationResult_HiddenWithMetadata_PublishesTooltipOnlyState()
    {
        var markerWindow = new FakeMarkerWindow();
        var coordinator = CreateCoordinator(markerWindow, new FakeBoundsReader(), out var request);

        coordinator.ApplyClassificationResult(
            ForegroundPrivilegeClassificationResult.Hidden(request)
            with
            {
                Metadata = new ForegroundPrivilegeMarkerMetadata("admin.exe", "S-1-admin")
            });

        Assert.False(coordinator.CurrentState.IsActive);
        Assert.Null(coordinator.CurrentState.Color);
        Assert.Null(coordinator.CurrentState.Kind);
        Assert.Null(coordinator.CurrentState.TooltipMode);
        Assert.Equal("admin.exe", coordinator.CurrentState.Metadata?.ProcessName);
        Assert.Equal("S-1-admin", coordinator.CurrentState.Metadata?.AccountSid);
        Assert.Equal(0, markerWindow.ShowCount);
    }

    [Fact]
    public void RefreshForegroundWindow_NewTarget_ClearsPreviousStateBeforeNextResult()
    {
        var markerWindow = new FakeMarkerWindow();
        var resolver = new QueueForegroundWindowResolver(
            new ForegroundWindowInfo((IntPtr)100, 200, "FirstWindow"),
            new ForegroundWindowInfo((IntPtr)101, 201, "SecondWindow"));
        var coordinator = new ForegroundPrivilegeRefreshCoordinator(
            resolver,
            Mock.Of<IProcessCreationTimeReader>(),
            new FakeBoundsReader(),
            CreateShellWindowFilter(),
            markerWindow,
            Mock.Of<ILoggingService>());
        var requests = new List<ForegroundPrivilegeClassificationRequest>();
        coordinator.ClassificationRequested += requests.Add;
        coordinator.SetRuntimeEnabled(true);
        coordinator.SetMarkerWindowEnabled(true);
        coordinator.RefreshForegroundWindow();
        coordinator.ApplyClassificationResult(
            ForegroundPrivilegeClassificationResult.Visible(requests[0], ForegroundPrivilegeMarkerKind.Basic));

        coordinator.RefreshForegroundWindow();

        Assert.Same(ForegroundPrivilegeMarkerState.Inactive, coordinator.CurrentState);
    }

    [Fact]
    public void RequestReclassification_KeepsPreviousStateBeforeNextResult()
    {
        var markerWindow = new FakeMarkerWindow();
        var coordinator = CreateCoordinator(markerWindow, new FakeBoundsReader(), out var request);
        coordinator.ApplyClassificationResult(
            ForegroundPrivilegeClassificationResult.Visible(request, ForegroundPrivilegeMarkerKind.Basic));
        var hideCountBeforeReclassification = markerWindow.HideCount;

        coordinator.RequestReclassification();

        Assert.True(coordinator.CurrentState.IsActive);
        Assert.Equal(ForegroundPrivilegeMarkerKind.Basic, coordinator.CurrentState.Kind);
        Assert.Equal(hideCountBeforeReclassification, markerWindow.HideCount);
    }

    [Fact]
    public void RefreshForegroundWindow_SameTargetKeepsPreviousStateBeforeNextResult()
    {
        var markerWindow = new FakeMarkerWindow();
        var coordinator = CreateCoordinator(markerWindow, new FakeBoundsReader(), out var request);
        coordinator.ApplyClassificationResult(
            ForegroundPrivilegeClassificationResult.Visible(request, ForegroundPrivilegeMarkerKind.Basic));
        var hideCountBeforeRefresh = markerWindow.HideCount;

        coordinator.RefreshForegroundWindow();

        Assert.True(coordinator.CurrentState.IsActive);
        Assert.Equal(ForegroundPrivilegeMarkerKind.Basic, coordinator.CurrentState.Kind);
        Assert.Equal(hideCountBeforeRefresh, markerWindow.HideCount);
    }

    [Fact]
    public void MoveSizeSuppression_HidesOnlyMarkerWindow()
    {
        var markerWindow = new FakeMarkerWindow();
        var coordinator = CreateCoordinator(markerWindow, new FakeBoundsReader(), out var request);
        coordinator.ApplyClassificationResult(
            ForegroundPrivilegeClassificationResult.Visible(request, ForegroundPrivilegeMarkerKind.LowIL)
            with
            {
                Metadata = new ForegroundPrivilegeMarkerMetadata("app.exe", "sid")
            });
        var hideCountBeforeMove = markerWindow.HideCount;

        coordinator.OnMoveSizeStarted((IntPtr)100);

        Assert.True(coordinator.CurrentState.IsActive);
        Assert.Equal(hideCountBeforeMove + 1, markerWindow.HideCount);
    }

    [Fact]
    public void MoveSizeEnded_RerendersWithoutRequestingClassification()
    {
        var markerWindow = new FakeMarkerWindow();
        var coordinator = CreateCoordinator(markerWindow, new FakeBoundsReader(), out var request);
        coordinator.ApplyClassificationResult(
            ForegroundPrivilegeClassificationResult.Visible(request, ForegroundPrivilegeMarkerKind.Basic));
        var showCountBeforeMove = markerWindow.ShowCount;

        coordinator.OnMoveSizeStarted((IntPtr)100);
        coordinator.OnMoveSizeEnded((IntPtr)100);

        Assert.Equal(showCountBeforeMove + 1, markerWindow.ShowCount);
        Assert.True(coordinator.CurrentState.IsActive);
    }

    [Fact]
    public void MarkerWindowDisabled_HidesOnlyWindowAndKeepsState()
    {
        var markerWindow = new FakeMarkerWindow();
        var coordinator = CreateCoordinator(markerWindow, new FakeBoundsReader(), out var request);
        coordinator.ApplyClassificationResult(
            ForegroundPrivilegeClassificationResult.Visible(request, ForegroundPrivilegeMarkerKind.Basic));
        var hideCountBeforeDisable = markerWindow.HideCount;

        coordinator.SetMarkerWindowEnabled(false);

        Assert.True(coordinator.CurrentState.IsActive);
        Assert.Equal(hideCountBeforeDisable + 1, markerWindow.HideCount);
    }

    [Fact]
    public void MarkerWindowReenabled_RerendersCurrentActiveMarker()
    {
        var markerWindow = new FakeMarkerWindow();
        var coordinator = CreateCoordinator(markerWindow, new FakeBoundsReader(), out var request);
        coordinator.ApplyClassificationResult(
            ForegroundPrivilegeClassificationResult.Visible(request, ForegroundPrivilegeMarkerKind.Basic));
        coordinator.SetMarkerWindowEnabled(false);
        var showCountBeforeEnable = markerWindow.ShowCount;

        coordinator.SetMarkerWindowEnabled(true);

        Assert.Equal(showCountBeforeEnable + 1, markerWindow.ShowCount);
    }

    [Fact]
    public void FullscreenDisabled_HidesOnlyMarkerWindowAndKeepsState()
    {
        var markerWindow = new FakeMarkerWindow();
        var boundsReader = new FakeBoundsReader { IsFullscreenResult = true };
        var coordinator = CreateCoordinator(markerWindow, boundsReader, out var request);
        coordinator.ApplyClassificationResult(
            ForegroundPrivilegeClassificationResult.Visible(request, ForegroundPrivilegeMarkerKind.Basic));
        var hideCountBeforeDisable = markerWindow.HideCount;

        coordinator.SetMarkerWindowEnabledWhenFullscreen(false);

        Assert.True(coordinator.CurrentState.IsActive);
        Assert.Equal(hideCountBeforeDisable + 1, markerWindow.HideCount);
    }

    [Fact]
    public void FullscreenReenabled_RerendersCurrentActiveMarker()
    {
        var markerWindow = new FakeMarkerWindow();
        var boundsReader = new FakeBoundsReader { IsFullscreenResult = true };
        var coordinator = CreateCoordinator(markerWindow, boundsReader, out var request);
        coordinator.ApplyClassificationResult(
            ForegroundPrivilegeClassificationResult.Visible(request, ForegroundPrivilegeMarkerKind.Basic));
        coordinator.SetMarkerWindowEnabledWhenFullscreen(false);
        var showCountBeforeEnable = markerWindow.ShowCount;

        coordinator.SetMarkerWindowEnabledWhenFullscreen(true);

        Assert.Equal(showCountBeforeEnable + 1, markerWindow.ShowCount);
    }

    [Fact]
    public void InvalidVisibleBounds_PublishesTooltipOnlyState()
    {
        var markerWindow = new FakeMarkerWindow();
        var boundsReader = new FakeBoundsReader { VisibleBoundsAvailable = false };
        var coordinator = CreateCoordinator(markerWindow, boundsReader, out var request);
        var states = new List<ForegroundPrivilegeMarkerState>();
        coordinator.StateChanged += states.Add;

        coordinator.ApplyClassificationResult(
            ForegroundPrivilegeClassificationResult.Visible(request, ForegroundPrivilegeMarkerKind.LowIL)
            with
            {
                Metadata = new ForegroundPrivilegeMarkerMetadata("lowil.exe", "S-1-lowil")
            });

        Assert.False(coordinator.CurrentState.IsActive);
        Assert.Equal(ForegroundPrivilegeMarkerKind.LowIL, coordinator.CurrentState.Kind);
        Assert.Equal(ForegroundPrivilegeTooltipMode.LowIL, coordinator.CurrentState.TooltipMode);
        Assert.Equal("lowil.exe", coordinator.CurrentState.Metadata?.ProcessName);
        Assert.DoesNotContain(states, state => state.IsActive);
    }

    [Fact]
    public void InvalidVisibleBounds_PublishesTooltipOnlyStateEvenWhenMarkerWindowDisabled()
    {
        var markerWindow = new FakeMarkerWindow();
        var boundsReader = new FakeBoundsReader { VisibleBoundsAvailable = true };
        var coordinator = CreateCoordinator(markerWindow, boundsReader, out var request);
        coordinator.SetMarkerWindowEnabled(false);

        boundsReader.VisibleBoundsAvailable = false;
        coordinator.ApplyClassificationResult(
            ForegroundPrivilegeClassificationResult.Visible(request, ForegroundPrivilegeMarkerKind.Basic)
            with
            {
                Metadata = new ForegroundPrivilegeMarkerMetadata("app.exe", "sid")
            });

        Assert.False(coordinator.CurrentState.IsActive);
        Assert.Equal(ForegroundPrivilegeMarkerKind.Basic, coordinator.CurrentState.Kind);
        Assert.Equal("app.exe", coordinator.CurrentState.Metadata?.ProcessName);
    }

    [Fact]
    public void ApplyClassificationResult_CurrentStaleResult_ClearsPublishedState()
    {
        var markerWindow = new FakeMarkerWindow();
        var coordinator = CreateCoordinator(markerWindow, new FakeBoundsReader(), out var request);
        coordinator.ApplyClassificationResult(
            ForegroundPrivilegeClassificationResult.Visible(request, ForegroundPrivilegeMarkerKind.Basic));

        coordinator.ApplyClassificationResult(
            ForegroundPrivilegeClassificationResult.Visible(request, ForegroundPrivilegeMarkerKind.Basic)
            with
            {
                IsStale = true
            });

        Assert.Same(ForegroundPrivilegeMarkerState.Inactive, coordinator.CurrentState);
    }

    [Fact]
    public void LocationChanged_WhenBoundsBecomeValid_RestoresActiveState()
    {
        var markerWindow = new FakeMarkerWindow();
        var boundsReader = new FakeBoundsReader { VisibleBoundsAvailable = false };
        var coordinator = CreateCoordinator(markerWindow, boundsReader, out var request);
        coordinator.ApplyClassificationResult(
            ForegroundPrivilegeClassificationResult.Visible(request, ForegroundPrivilegeMarkerKind.Basic));

        boundsReader.VisibleBoundsAvailable = true;
        coordinator.OnLocationChanged((IntPtr)100);

        Assert.True(coordinator.CurrentState.IsActive);
        Assert.Equal(ForegroundPrivilegeMarkerKind.Basic, coordinator.CurrentState.Kind);
    }

    [Fact]
    public void LocationChanged_WhenActiveBoundsBecomeInvalid_PublishesTooltipOnlyState()
    {
        var markerWindow = new FakeMarkerWindow();
        var boundsReader = new FakeBoundsReader { VisibleBoundsAvailable = true };
        var coordinator = CreateCoordinator(markerWindow, boundsReader, out var request);
        var states = new List<ForegroundPrivilegeMarkerState>();
        coordinator.StateChanged += states.Add;

        coordinator.ApplyClassificationResult(
            ForegroundPrivilegeClassificationResult.Visible(request, ForegroundPrivilegeMarkerKind.Basic));

        Assert.True(coordinator.CurrentState.IsActive);
        var hideCountBefore = markerWindow.HideCount;

        boundsReader.VisibleBoundsAvailable = false;
        coordinator.OnLocationChanged((IntPtr)100);

        Assert.False(coordinator.CurrentState.IsActive);
        Assert.Equal(ForegroundPrivilegeMarkerKind.Basic, coordinator.CurrentState.Kind);
        Assert.Equal("PID 200", coordinator.CurrentState.Metadata?.ProcessName);
        Assert.Equal(coordinator.CurrentState, states[^1]);
        Assert.Contains(states, state => state.IsActive);
        Assert.True(states.Count >= 2);
        Assert.True(markerWindow.HideCount > hideCountBefore);
    }

    [Fact]
    public void LocationChanged_WhenActiveBoundsBecomeInvalidAndMarkerWindowDisabled_PublishesTooltipOnlyState()
    {
        var markerWindow = new FakeMarkerWindow();
        var boundsReader = new FakeBoundsReader { VisibleBoundsAvailable = true };
        var coordinator = CreateCoordinator(markerWindow, boundsReader, out var request);
        var states = new List<ForegroundPrivilegeMarkerState>();
        coordinator.StateChanged += states.Add;

        coordinator.ApplyClassificationResult(
            ForegroundPrivilegeClassificationResult.Visible(request, ForegroundPrivilegeMarkerKind.Basic));
        coordinator.SetMarkerWindowEnabled(false);
        Assert.True(coordinator.CurrentState.IsActive);
        var hideCountBefore = markerWindow.HideCount;

        boundsReader.VisibleBoundsAvailable = false;
        coordinator.OnLocationChanged((IntPtr)100);

        Assert.False(coordinator.CurrentState.IsActive);
        Assert.Equal(ForegroundPrivilegeMarkerKind.Basic, coordinator.CurrentState.Kind);
        Assert.Equal("PID 200", coordinator.CurrentState.Metadata?.ProcessName);
        Assert.Equal(coordinator.CurrentState, states[^1]);
        Assert.Contains(states, state => state.IsActive);
        Assert.True(states.Count >= 2);
        Assert.True(markerWindow.HideCount > hideCountBefore);
    }

    [Fact]
    public void Dispose_PublishesInactiveCleanup()
    {
        var markerWindow = new FakeMarkerWindow();
        var coordinator = CreateCoordinator(markerWindow, new FakeBoundsReader(), out var request);
        coordinator.ApplyClassificationResult(
            ForegroundPrivilegeClassificationResult.Visible(request, ForegroundPrivilegeMarkerKind.Basic));

        coordinator.Dispose();

        Assert.Same(ForegroundPrivilegeMarkerState.Inactive, coordinator.CurrentState);
    }

    [Theory]
    [InlineData(ResultMismatch.RequestId)]
    [InlineData(ResultMismatch.EnabledGeneration)]
    [InlineData(ResultMismatch.TrackedWindowHandle)]
    [InlineData(ResultMismatch.PrivilegeSubjectProcessId)]
    [InlineData(ResultMismatch.Stale)]
    public void ApplyClassificationResult_IgnoresStaleOrMismatchedResults(ResultMismatch mismatch)
    {
        var markerWindow = new FakeMarkerWindow();
        var coordinator = CreateCoordinator(markerWindow, new FakeBoundsReader(), out var request);

        coordinator.ApplyClassificationResult(CreateResult(request, mismatch));

        Assert.Equal(0, markerWindow.ShowCount);
    }

    [Fact]
    public void ApplyClassificationResult_CurrentResultWithMismatchedCreationTime_ClearsPublishedState()
    {
        var markerWindow = new FakeMarkerWindow();
        var processCreationTimeReader = new Mock<IProcessCreationTimeReader>();
        processCreationTimeReader.Setup(r => r.TryGetProcessCreationTimeUtcTicks(200, out It.Ref<long>.IsAny))
            .Returns((uint _, out long creationTimeUtcTicks) =>
            {
                creationTimeUtcTicks = 999;
                return true;
            });
        var coordinator = CreateCoordinator(markerWindow, new FakeBoundsReader(), processCreationTimeReader.Object, out var request);
        coordinator.ApplyClassificationResult(
            ForegroundPrivilegeClassificationResult.Visible(request, ForegroundPrivilegeMarkerKind.Basic));
        var result = ForegroundPrivilegeClassificationResult.Visible(request, ForegroundPrivilegeMarkerKind.Basic)
            with { PrivilegeSubjectCreationTimeUtcTicks = 123 };

        coordinator.ApplyClassificationResult(result);

        Assert.Same(ForegroundPrivilegeMarkerState.Inactive, coordinator.CurrentState);
    }

    private static ForegroundPrivilegeRefreshCoordinator CreateCoordinator(
        FakeMarkerWindow markerWindow,
        FakeBoundsReader boundsReader,
        out ForegroundPrivilegeClassificationRequest request)
        => CreateCoordinator(markerWindow, boundsReader, Mock.Of<IProcessCreationTimeReader>(), out request);

    private static ForegroundPrivilegeRefreshCoordinator CreateCoordinator(
        FakeMarkerWindow markerWindow,
        FakeBoundsReader boundsReader,
        IProcessCreationTimeReader processCreationTimeReader,
        out ForegroundPrivilegeClassificationRequest request)
    {
        var resolver = new Mock<IForegroundWindowResolver>();
        resolver.Setup(r => r.GetForegroundWindow())
            .Returns(new ForegroundWindowInfo((IntPtr)100, 200, "TestWindow"));
        var coordinator = new ForegroundPrivilegeRefreshCoordinator(
            resolver.Object,
            processCreationTimeReader,
            boundsReader,
            CreateShellWindowFilter(),
            markerWindow,
            Mock.Of<ILoggingService>());
        var capturedRequest = default(ForegroundPrivilegeClassificationRequest);
        coordinator.ClassificationRequested += r => capturedRequest = r;
        coordinator.SetRuntimeEnabled(true);
        coordinator.SetMarkerWindowEnabled(true);
        coordinator.RefreshForegroundWindow();
        request = capturedRequest;
        return coordinator;
    }

    private static ForegroundPrivilegeClassificationResult CreateResult(
        ForegroundPrivilegeClassificationRequest request,
        ResultMismatch mismatch)
    {
        var result = ForegroundPrivilegeClassificationResult.Visible(request, ForegroundPrivilegeMarkerKind.Basic);
        return mismatch switch
        {
            ResultMismatch.RequestId => result with { RequestId = request.RequestId + 1 },
            ResultMismatch.EnabledGeneration => result with { EnabledGeneration = request.EnabledGeneration + 1 },
            ResultMismatch.TrackedWindowHandle => result with { TrackedWindowHandle = request.TrackedWindowHandle + 1 },
            ResultMismatch.PrivilegeSubjectProcessId => result with { PrivilegeSubjectProcessId = request.PrivilegeSubjectProcessId + 1 },
            ResultMismatch.Stale => result with { IsStale = true },
            _ => throw new ArgumentOutOfRangeException(nameof(mismatch)),
        };
    }

    [Theory]
    [InlineData("SearchHost.exe")]
    [InlineData("StartMenuExperienceHost.exe")]
    public void RefreshForegroundWindow_SystemAppsShellProcess_DoesNotRequestClassification(string processName)
    {
        var markerWindow = new FakeMarkerWindow();
        var resolver = new Mock<IForegroundWindowResolver>();
        resolver.Setup(r => r.GetForegroundWindow())
            .Returns(new ForegroundWindowInfo((IntPtr)100, 200, "Windows.UI.Core.CoreWindow"));
        var processImagePathReader = new Mock<IProcessImagePathReader>();
        processImagePathReader.Setup(r => r.TryGetProcessImagePath(200))
            .Returns(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                "SystemApps",
                "MicrosoftWindows.Client.CBS_cw5n1h2txyewy",
                processName));
        var coordinator = new ForegroundPrivilegeRefreshCoordinator(
            resolver.Object,
            Mock.Of<IProcessCreationTimeReader>(),
            new FakeBoundsReader(),
            new ForegroundShellWindowFilter(processImagePathReader.Object),
            markerWindow,
            Mock.Of<ILoggingService>());
        var requestCount = 0;
        coordinator.ClassificationRequested += _ => requestCount++;
        coordinator.SetRuntimeEnabled(true);
        coordinator.SetMarkerWindowEnabled(true);
        var hideCountBeforeRefresh = markerWindow.HideCount;

        coordinator.RefreshForegroundWindow();

        Assert.Equal(0, requestCount);
        Assert.Same(ForegroundPrivilegeMarkerState.Inactive, coordinator.CurrentState);
        Assert.Equal(hideCountBeforeRefresh + 1, markerWindow.HideCount);
    }

    [Theory]
    [InlineData("SomeStoreApp.exe")]
    [InlineData("SearchHost.exe")]
    public void RefreshForegroundWindow_NonSystemAppsWindow_StillRequestsClassification(string processName)
    {
        var markerWindow = new FakeMarkerWindow();
        var resolver = new Mock<IForegroundWindowResolver>();
        resolver.Setup(r => r.GetForegroundWindow())
            .Returns(new ForegroundWindowInfo((IntPtr)100, 200, "Windows.UI.Core.CoreWindow"));
        var processImagePathReader = new Mock<IProcessImagePathReader>();
        processImagePathReader.Setup(r => r.TryGetProcessImagePath(200))
            .Returns(Path.Combine(@"C:\Apps", processName));
        var coordinator = new ForegroundPrivilegeRefreshCoordinator(
            resolver.Object,
            Mock.Of<IProcessCreationTimeReader>(),
            new FakeBoundsReader(),
            new ForegroundShellWindowFilter(processImagePathReader.Object),
            markerWindow,
            Mock.Of<ILoggingService>());
        var requestCount = 0;
        coordinator.ClassificationRequested += _ => requestCount++;
        coordinator.SetRuntimeEnabled(true);
        coordinator.SetMarkerWindowEnabled(true);

        coordinator.RefreshForegroundWindow();

        Assert.Equal(1, requestCount);
    }

    public enum ResultMismatch
    {
        RequestId,
        EnabledGeneration,
        TrackedWindowHandle,
        PrivilegeSubjectProcessId,
        Stale,
    }

    private sealed class FakeBoundsReader : IForegroundWindowBoundsReader
    {
        public bool VisibleBoundsAvailable { get; set; } = true;
        public bool IsFullscreenResult { get; set; }

        public IntPtr ResolveTrackedTopLevelWindow(IntPtr hwnd) => hwnd;

        public bool TryGetVisibleBounds(IntPtr hwnd, out Rectangle bounds)
        {
            bounds = new Rectangle(20, 20, 300, 200);
            return VisibleBoundsAvailable;
        }

        public bool IsFullscreen(IntPtr hwnd, Rectangle bounds) => IsFullscreenResult;

        public bool ShouldRenderInsideLeftEdge(Rectangle bounds) => false;
    }

    private sealed class QueueForegroundWindowResolver : IForegroundWindowResolver
    {
        private readonly Queue<ForegroundWindowInfo> queue;
        private ForegroundWindowInfo current;

        public QueueForegroundWindowResolver(params ForegroundWindowInfo[] windows)
        {
            queue = new Queue<ForegroundWindowInfo>(windows);
            current = queue.Peek();
        }

        public ForegroundWindowInfo GetForegroundWindow()
        {
            current = queue.Dequeue();
            return current;
        }

        public ForegroundWindowInfo GetWindowInfo(IntPtr hwnd) => current;
    }

    private static ForegroundShellWindowFilter CreateShellWindowFilter() =>
        new(Mock.Of<IProcessImagePathReader>());

    private sealed class FakeMarkerWindow : IForegroundMarkerWindow
    {
        public int ShowCount { get; private set; }
        public int HideCount { get; private set; }
        public Color LastColor { get; private set; }
        public IntPtr LastTargetWindow { get; private set; }

        public void Show(IntPtr targetWindow, Rectangle bounds, bool renderInsideLeftEdge, Color color)
        {
            ShowCount++;
            LastTargetWindow = targetWindow;
            LastColor = color;
        }

        public void Hide() => HideCount++;

        public void Dispose()
        {
        }
    }
}
