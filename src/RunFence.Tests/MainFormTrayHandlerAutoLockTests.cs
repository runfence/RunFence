using Moq;
using RunFence.Account;
using RunFence.Account.UI;
using RunFence.Apps.Shortcuts;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.DragBridge;
using RunFence.Infrastructure;
using RunFence.Launch;
using RunFence.Licensing;
using RunFence.Persistence;
using RunFence.Persistence.UI;
using RunFence.Security;
using RunFence.Startup.UI;
using RunFence.TrayIcon;
using RunFence.UI;
using Xunit;

namespace RunFence.Tests;

/// <summary>
/// Tests auto-lock coordination behavior through <see cref="MainFormBackgroundAutoLockCoordinator"/>
/// and show-window/unlock request handling through <see cref="MainFormWindowRequestHandler"/>.
/// </summary>
public class MainFormTrayHandlerAutoLockTests : IDisposable
{
    private readonly Mock<IAutoLockTimerService> _autoLockTimer = new();
    private readonly Mock<ILicenseService> _licenseService = new();
    private readonly FakeForm _fakeForm = new();

    private readonly AppDatabase _database = new();
    private readonly ProtectedBuffer _pinKey;
    private readonly LockManager _lockManager;
    private readonly MainFormWindowRequestHandler _windowRequestHandler;
    private readonly MainFormBackgroundAutoLockCoordinator _autoLockCoordinator;
    private readonly MainFormTrayHandler _handler;
    private readonly SessionContext _session;

    public MainFormTrayHandlerAutoLockTests()
    {
        _pinKey = new ProtectedBuffer(new byte[32], protect: false);
        _session = new SessionContext
        {
            Database = _database,
            CredentialStore = new CredentialStore(),
            PinDerivedKey = _pinKey
        };
        _database.Settings.AutoLockInBackground = true;
        _database.Settings.AutoLockTimeoutMinutes = 5;

        _lockManager = new LockManager(
            _session,
            new Mock<IPinService>().Object,
            new Mock<ILoggingService>().Object,
            new Mock<ISecureDesktopRunner>().Object,
            new Mock<IWindowsHelloService>().Object,
            _autoLockTimer.Object,
            new Mock<IUnlockProcessLauncher>().Object);

        _licenseService.Setup(l => l.IsLicensed).Returns(true);
        _fakeForm.Visible = true;

        (_windowRequestHandler, _autoLockCoordinator, _handler) = CreateHandler();
    }

    public void Dispose()
    {
        _handler.Dispose();
        _pinKey.Dispose();
    }

    private sealed class FakeForm : IMainFormVisibility, ITrayOwner
    {
        public Action? ShowCallback { get; set; }
        public bool Visible { get; set; } = true;
        public bool IsModalActive { get; set; }
        public bool HasOtherWindowsOpen { get; set; }
        public bool IsDisposed => false;
        public bool IsHandleCreated => false;
        public FormWindowState WindowState { get; set; }
        public bool ShowInTaskbar { get; set; }
        public string Title { set { } }
        public IntPtr Handle => IntPtr.Zero;

        public void Show()
        {
            Visible = true;
            ShowCallback?.Invoke();
        }

        public void Hide() => Visible = false;

        public void BringToFront() { }

        public void BeginInvokeOnUiThread(Action action) => action();

        public void InvokeOnUiThread(Action action) => action();

        public Task TryShowWindowAsync() => Task.CompletedTask;
    }

    // --- MainFormBackgroundAutoLockCoordinator tests ---

    [Fact]
    public void HandleAppDeactivated_WhenLocked_DoesNotStartTimer()
    {
        _lockManager.LockWindow();

        _autoLockCoordinator.HandleAppDeactivated();

        _autoLockTimer.Verify(t => t.Start(It.IsAny<int>(), It.IsAny<Action>()), Times.Never);
    }

    [Fact]
    public void HandleAppDeactivated_WhenFormNotVisible_DoesNotStartTimer()
    {
        _fakeForm.Visible = false;

        _autoLockCoordinator.HandleAppDeactivated();

        _autoLockTimer.Verify(t => t.Start(It.IsAny<int>(), It.IsAny<Action>()), Times.Never);
    }

    [Fact]
    public void HandleAppDeactivated_WhenModalActive_DoesNotStartTimer()
    {
        _fakeForm.IsModalActive = true;

        _autoLockCoordinator.HandleAppDeactivated();

        _autoLockTimer.Verify(t => t.Start(It.IsAny<int>(), It.IsAny<Action>()), Times.Never);
    }

    [Fact]
    public void HandleAppDeactivated_WhenOtherWindowOpen_DoesNotStartTimer()
    {
        _fakeForm.HasOtherWindowsOpen = true;

        _autoLockCoordinator.HandleAppDeactivated();

        _autoLockTimer.Verify(t => t.Start(It.IsAny<int>(), It.IsAny<Action>()), Times.Never);
    }

    [Fact]
    public void HandleAppDeactivated_WhenNotLicensed_DoesNotStartTimer()
    {
        _licenseService.Setup(l => l.IsLicensed).Returns(false);

        _autoLockCoordinator.HandleAppDeactivated();

        _autoLockTimer.Verify(t => t.Start(It.IsAny<int>(), It.IsAny<Action>()), Times.Never);
    }

    [Fact]
    public void HandleAppDeactivated_ValidState_StartsBackgroundTimer()
    {
        _autoLockCoordinator.HandleAppDeactivated();

        _autoLockTimer.Verify(t => t.Start(300, It.IsAny<Action>()), Times.Once);
    }

    [Fact]
    public void HandleAppActivated_StopsTimer()
    {
        _autoLockCoordinator.HandleAppDeactivated();

        _autoLockCoordinator.HandleAppActivated();

        _autoLockTimer.Verify(t => t.Stop(), Times.AtLeastOnce);
    }

    [Fact]
    public void HideToTray_WhenTimeoutIsZero_LocksImmediately()
    {
        _database.Settings.AutoLockTimeoutMinutes = 0;
        _autoLockCoordinator.HideToTray();

        Assert.True(_lockManager.IsLocked);
        Assert.False(_fakeForm.Visible);
        _autoLockTimer.Verify(t => t.Start(It.IsAny<int>(), It.IsAny<Action>()), Times.Never);
    }

    [Fact]
    public void ShowAndActivateForUnlock_ReentrantResize_DoesNotHideToTrayOrStartLockTimer()
    {
        _fakeForm.Visible = false;
        _fakeForm.WindowState = FormWindowState.Minimized;
        _fakeForm.ShowInTaskbar = false;
        _fakeForm.ShowCallback = () => _autoLockCoordinator.HandleResize();

        _windowRequestHandler.ShowAndActivateForUnlock();

        Assert.True(_fakeForm.Visible);
        Assert.Equal(FormWindowState.Normal, _fakeForm.WindowState);
        Assert.True(_fakeForm.ShowInTaskbar);
        _autoLockTimer.Verify(t => t.Start(It.IsAny<int>(), It.IsAny<Action>()), Times.Never);
    }

    [Fact]
    public void HandleWindowlessUnlock_WhenHiddenAndLicensed_StartsBackgroundTimer()
    {
        _fakeForm.Visible = false;

        _autoLockCoordinator.HandleWindowlessUnlock();

        _autoLockTimer.Verify(t => t.Start(300, It.IsAny<Action>()), Times.Once);
    }

    [Fact]
    public void HandleWindowlessUnlock_WhenVisible_DoesNotStartBackgroundTimer()
    {
        _fakeForm.Visible = true;

        _autoLockCoordinator.HandleWindowlessUnlock();

        _autoLockTimer.Verify(t => t.Start(It.IsAny<int>(), It.IsAny<Action>()), Times.Never);
    }

    [Fact]
    public void HandleWindowlessUnlock_WhenHiddenAndUnlicensed_DoesNotStartBackgroundTimer()
    {
        _fakeForm.Visible = false;
        _licenseService.Setup(l => l.IsLicensed).Returns(false);

        _autoLockCoordinator.HandleWindowlessUnlock();

        _autoLockTimer.Verify(t => t.Start(It.IsAny<int>(), It.IsAny<Action>()), Times.Never);
    }

    // --- MainFormWindowRequestHandler tests ---

    [Fact]
    public async Task HandleElevatedUnlockRequestAsync_WhenAlreadyUnlocked_UsesNormalShowFlow()
    {
        _windowRequestHandler.SetStartupComplete();

        var result = await _windowRequestHandler.HandleElevatedUnlockRequestAsync();

        Assert.True(result);
        _autoLockTimer.Verify(t => t.Stop(), Times.AtLeastOnce);
    }

    [Fact]
    public async Task HandleOperationUnlockRequestAsync_WhenAlreadyUnlocked_ReturnsSuccessWithoutShowingWindow()
    {
        _windowRequestHandler.SetStartupComplete();
        _fakeForm.Visible = false;

        var result = await _windowRequestHandler.HandleOperationUnlockRequestAsync();

        Assert.True(result);
        Assert.False(_fakeForm.Visible);
    }

    [Fact]
    public void RequestOperationUnlock_WhenStartupComplete_StartsOperationUnlockFlow()
    {
        _database.Settings.UnlockMode = UnlockMode.Admin;
        using var launchCalled = new ManualResetEventSlim();
        var unlockProcessLauncher = new Mock<IUnlockProcessLauncher>();
        unlockProcessLauncher
            .Setup(l => l.LaunchUnlockProcess(true))
            .Callback(launchCalled.Set);
        var (windowRequestHandler, _, handler) = CreateHandler(
            autoLockTimer: new Mock<IAutoLockTimerService>().Object,
            unlockProcessLauncher: unlockProcessLauncher.Object,
            startLocked: true);
        using var _ = handler;

        windowRequestHandler.RequestOperationUnlock();

        Assert.True(launchCalled.Wait(1000));
        unlockProcessLauncher.Verify(l => l.LaunchUnlockProcess(true), Times.Once);
    }

    [Fact]
    public void RequestShowWindow_BeforeStartupComplete_RunsAfterStartupCompletes()
    {
        using var shown = new ManualResetEventSlim();
        var autoLockTimer = new Mock<IAutoLockTimerService>();
        autoLockTimer.Setup(t => t.Stop()).Callback(shown.Set);

        var lockManager = new LockManager(
            _session,
            new Mock<IPinService>().Object,
            new Mock<ILoggingService>().Object,
            new Mock<ISecureDesktopRunner>().Object,
            new Mock<IWindowsHelloService>().Object,
            autoLockTimer.Object,
            new Mock<IUnlockProcessLauncher>().Object);

        var windowRequestHandler = new MainFormWindowRequestHandler(
            lockManager,
            new Mock<IConfigAvailabilityChecker>().Object,
            new Mock<IShellHelper>().Object,
            _licenseService.Object,
            new Mock<ILoggingService>().Object);
        windowRequestHandler.Initialize(_fakeForm);

        windowRequestHandler.RequestShowWindow();

        autoLockTimer.Verify(t => t.Stop(), Times.Never);

        windowRequestHandler.SetStartupComplete();

        Assert.True(shown.Wait(1000));
    }

    private (MainFormWindowRequestHandler windowRequestHandler, MainFormBackgroundAutoLockCoordinator autoLockCoordinator, MainFormTrayHandler handler) CreateHandler(
        IAutoLockTimerService? autoLockTimer = null,
        IUnlockProcessLauncher? unlockProcessLauncher = null,
        bool startLocked = false)
    {
        var lockManager = autoLockTimer == null && unlockProcessLauncher == null ? _lockManager : new LockManager(
            _session,
            new Mock<IPinService>().Object,
            new Mock<ILoggingService>().Object,
            new Mock<ISecureDesktopRunner>().Object,
            new Mock<IWindowsHelloService>().Object,
            autoLockTimer ?? _autoLockTimer.Object,
            unlockProcessLauncher ?? new Mock<IUnlockProcessLauncher>().Object);
        if (startLocked)
            lockManager.LockWindow();

        var windowRequestHandler = new MainFormWindowRequestHandler(
            lockManager,
            new Mock<IConfigAvailabilityChecker>().Object,
            new Mock<IShellHelper>().Object,
            _licenseService.Object,
            new Mock<ILoggingService>().Object);

        var autoLockCoordinator = new MainFormBackgroundAutoLockCoordinator(
            lockManager,
            windowRequestHandler,
            _licenseService.Object);

        var notifyIcon = new NotifyIcon();

        var mockAppIconProvider = new Mock<IAppIconProvider>();
        mockAppIconProvider.Setup(p => p.GetAppIcon()).Returns(SystemIcons.Application);

        var mockDbProvider = new Mock<IDatabaseProvider>();
        mockDbProvider.Setup(p => p.GetDatabase()).Returns(_database);

        var mockSidResolver = new Mock<ISidResolver>();
        var trayIconManager = new TrayIconManager(
            notifyIcon,
            new SidDisplayNameResolver(mockSidResolver.Object, new Mock<IProfilePathResolver>().Object),
            new Mock<IIconService>().Object,
            mockAppIconProvider.Object,
            mockDbProvider.Object,
            new TrayMenuDiscoveryBuilder(new Mock<IShortcutIconHelper>().Object),
            new Mock<IInputInjectionBlockerService>().Object);

        var accountToolResolver = new AccountToolResolver(new Mock<IProfilePathResolver>().Object);
        var mockLaunchFacade = new Mock<ILaunchFacade>();
        var packageInstallService = new PackageInstallService(
            mockLaunchFacade.Object,
            accountToolResolver,
            new Mock<ILoggingService>().Object);
        var trayLaunchHandler = new TrayLaunchHandler(
            new Mock<IAppEntryLauncher>().Object,
            mockLaunchFacade.Object,
            new ToolLauncher(
                mockLaunchFacade.Object,
                accountToolResolver,
                packageInstallService,
                new Mock<ILoggingService>().Object),
            new Mock<ISidNameCacheService>().Object,
            new Mock<ILoggingService>().Object);

        var discoveryRefreshManager = new DiscoveryRefreshManager(
            new StartMenuDiscoveryService(new Mock<IProfilePathResolver>().Object, new Mock<IShortcutComHelper>().Object, new Mock<ILoggingService>().Object),
            new Mock<ISessionProvider>().Object,
            new Mock<ILoggingService>().Object);

        var handler = new MainFormTrayHandler(
            lockManager,
            trayLaunchHandler,
            notifyIcon,
            trayIconManager,
            new Mock<IIdleMonitorService>().Object,
            discoveryRefreshManager,
            _session,
            _licenseService.Object,
            new Mock<IGlobalHotkeyService>().Object,
            windowRequestHandler,
            autoLockCoordinator);

        windowRequestHandler.Initialize(_fakeForm);
        autoLockCoordinator.Initialize(_fakeForm);
        windowRequestHandler.SetStartupComplete();

        return (windowRequestHandler, autoLockCoordinator, handler);
    }
}
