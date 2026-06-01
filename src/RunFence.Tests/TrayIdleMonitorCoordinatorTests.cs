using Moq;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Licensing;
using RunFence.UI;
using Xunit;

namespace RunFence.Tests;

public class TrayIdleMonitorCoordinatorTests
{
    [Fact]
    public void HandleIdleTimeout_WhenNotInitialized_Throws()
    {
        using var session = CreateSession();
        var coordinator = new TrayIdleMonitorCoordinator(
            Mock.Of<IIdleMonitorService>(),
            session,
            Mock.Of<ILicenseService>(),
            Mock.Of<IApplicationExitService>());

        var ex = Assert.Throws<InvalidOperationException>(coordinator.HandleIdleTimeout);

        Assert.Equal("TrayIdleMonitorCoordinator must be initialized before use.", ex.Message);
    }

    [Fact]
    public void HandleIdleTimeout_WhenFormDisposed_DoesNotExit()
    {
        using var session = CreateSession();
        var exitService = new Mock<IApplicationExitService>(MockBehavior.Strict);
        var coordinator = new TrayIdleMonitorCoordinator(
            Mock.Of<IIdleMonitorService>(),
            session,
            Mock.Of<ILicenseService>(service => service.IsLicensed == true),
            exitService.Object);
        coordinator.Initialize(new FakeMainFormVisibility { IsDisposed = true });

        coordinator.HandleIdleTimeout();

        exitService.Verify(service => service.Exit(), Times.Never);
    }

    [Fact]
    public void HandleIdleTimeout_WhenUnlicensed_DoesNotExit()
    {
        using var session = CreateSession();
        var exitService = new Mock<IApplicationExitService>(MockBehavior.Strict);
        var coordinator = new TrayIdleMonitorCoordinator(
            Mock.Of<IIdleMonitorService>(),
            session,
            Mock.Of<ILicenseService>(service => service.IsLicensed == false),
            exitService.Object);
        coordinator.Initialize(new FakeMainFormVisibility());

        coordinator.HandleIdleTimeout();

        exitService.Verify(service => service.Exit(), Times.Never);
    }

    [Fact]
    public void HandleIdleTimeout_WhenLicensedAndLive_ExitsExactlyOnce()
    {
        using var session = CreateSession();
        var exitService = new Mock<IApplicationExitService>(MockBehavior.Strict);
        exitService.Setup(service => service.Exit());
        var coordinator = new TrayIdleMonitorCoordinator(
            Mock.Of<IIdleMonitorService>(),
            session,
            Mock.Of<ILicenseService>(service => service.IsLicensed == true),
            exitService.Object);
        coordinator.Initialize(new FakeMainFormVisibility());

        coordinator.HandleIdleTimeout();

        exitService.Verify(service => service.Exit(), Times.Once);
    }

    private static SessionContext CreateSession()
        => new SessionContext
        {
            Database = new AppDatabase(),
            CredentialStore = new CredentialStore()
        }.WithPinDerivedKeyTakingOwnership(TestSecretFactory.Create(32));

    private sealed class FakeMainFormVisibility : IMainFormVisibility
    {
        public bool IsDisposed { get; set; }
        public bool IsHandleCreated => false;
        public bool Visible => true;
        public FormWindowState WindowState { get; set; }
        public bool ShowInTaskbar { get; set; }
        public bool IsModalActive => false;
        public bool HasOtherWindowsOpen => false;
        public IntPtr Handle => IntPtr.Zero;
        public string Title { set { } }

        public void Show() { }
        public void Hide() { }
        public void BringToFront() { }
        public void BeginInvokeOnUiThread(Action action) => action();
        public void InvokeOnUiThread(Action action) => action();
    }
}
