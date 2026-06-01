using Moq;
using RunFence.ForegroundMarker;
using RunFence.Infrastructure;
using RunFence.Startup;
using Xunit;

namespace RunFence.Tests;

public sealed class ForegroundPrivilegeMarkerServiceTests
{
    [Fact]
    public void StateSource_DelegatesEventAndPropertyToRuntime()
    {
        var runtime = new FakeRuntime();
        var service = CreateService(runtime, out _);
        ForegroundPrivilegeMarkerState? observedState = null;

        service.StateChanged += state =>
        {
            observedState = state;
            Assert.Same(state, service.CurrentState);
        };

        runtime.RaiseStateChanged(ForegroundPrivilegeMarkerState.Active(
            ForegroundPrivilegeMarkerKind.LowIL,
            ForegroundPrivilegeMarkerPalette.LowIntegrity,
            new ForegroundPrivilegeMarkerMetadata("app.exe", "sid")));

        Assert.NotNull(observedState);
        Assert.True(observedState!.IsActive);
        service.Dispose();
    }

    [Fact]
    public void InteractiveUserRefreshed_RequestsImmediateReclassification()
    {
        var runtime = new Mock<IForegroundPrivilegeMarkerRuntime>(MockBehavior.Strict);
        runtime.Setup(r => r.RequestReclassification());
        runtime.Setup(r => r.Dispose());
        runtime.SetupGet(r => r.CurrentState).Returns(ForegroundPrivilegeMarkerState.Inactive);
        var service = CreateService(runtime.Object, out var coordinator);

        coordinator.Refresh();

        runtime.Verify(r => r.RequestReclassification(), Times.Once);
        service.Dispose();
    }

    [Fact]
    public void Dispose_UnsubscribesBeforeStoppingRuntime()
    {
        var runtime = new Mock<IForegroundPrivilegeMarkerRuntime>(MockBehavior.Strict);
        InteractiveUserRefreshCoordinator? coordinator = null;
        runtime.Setup(r => r.Dispose())
            .Callback(() => coordinator!.Refresh());
        runtime.SetupGet(r => r.CurrentState).Returns(ForegroundPrivilegeMarkerState.Inactive);
        var service = CreateService(runtime.Object, out coordinator);

        service.Dispose();

        runtime.Verify(r => r.RequestReclassification(), Times.Never);
        runtime.Verify(r => r.Dispose(), Times.Once);
    }

    private static ForegroundPrivilegeMarkerService CreateService(
        IForegroundPrivilegeMarkerRuntime runtime,
        out InteractiveUserRefreshCoordinator coordinator)
    {
        var sidCache = new Mock<IInteractiveUserSidCache>();
        var desktopProvider = new Mock<IInteractiveUserDesktopProvider>();
        coordinator = new InteractiveUserRefreshCoordinator(sidCache.Object, desktopProvider.Object);
        return new ForegroundPrivilegeMarkerService(runtime, coordinator);
    }

    private sealed class FakeRuntime : IForegroundPrivilegeMarkerRuntime
    {
        public event Action<ForegroundPrivilegeMarkerState>? StateChanged;

        public ForegroundPrivilegeMarkerState CurrentState { get; private set; } = ForegroundPrivilegeMarkerState.Inactive;

        public void RaiseStateChanged(ForegroundPrivilegeMarkerState state)
        {
            CurrentState = state;
            StateChanged?.Invoke(state);
        }

        public void Start(bool markerWindowEnabled, bool markerWindowEnabledWhenFullscreen)
        {
        }

        public void Stop()
        {
        }

        public void SetMarkerWindowEnabled(bool enabled)
        {
        }

        public void SetMarkerWindowEnabledWhenFullscreen(bool enabled)
        {
        }

        public void RefreshForegroundWindow()
        {
        }

        public void RequestReclassification()
        {
        }

        public void Dispose()
        {
        }
    }
}
