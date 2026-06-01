using Moq;
using RunFence.Account;
using RunFence.Account.UI;
using RunFence.Apps.Shortcuts;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.DragBridge;
using RunFence.ForegroundMarker;
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
    private readonly SecureSecret _pinKey;
    private readonly LockManager _lockManager;
    private readonly MainFormWindowRequestHandler _windowRequestHandler;
    private readonly MainFormBackgroundAutoLockCoordinator _autoLockCoordinator;
    private readonly MainFormTrayHandler _handler;
    private readonly SessionContext _session;

    public MainFormTrayHandlerAutoLockTests()
    {
        _pinKey = TestSecretFactory.Create(32);
        _session = new SessionContext
{
            Database = _database,
            CredentialStore = new CredentialStore(),
        }.WithClonedPinDerivedKey(_pinKey);
        _database.Settings.AutoLockInBackground = true;
        _database.Settings.AutoLockTimeoutMinutes = 5;
        var credentialUnlockService = new Mock<ICredentialUnlockService>();
        credentialUnlockService.Setup(c => c.VerifyPin()).Returns(CredentialUnlockResult.Canceled);
        credentialUnlockService.Setup(c => c.VerifyAsync(It.IsAny<CredentialUnlockMode>())).ReturnsAsync(CredentialUnlockResult.Canceled);

        _lockManager = new LockManager(
            _session,
            new Mock<ILoggingService>().Object,
            _autoLockTimer.Object,
            new Mock<IUnlockProcessLauncher>().Object,
            new LockStateService(_session),
            credentialUnlockService.Object,
            new InlineUiThreadInvoker(a => a()),
            TimeSpan.FromMinutes(5));

        _licenseService.Setup(l => l.IsLicensed).Returns(true);
        _fakeForm.Visible = true;

        var handlerContext = CreateHandlerContext();
        _windowRequestHandler = handlerContext.WindowRequestHandler;
        _autoLockCoordinator = handlerContext.AutoLockCoordinator;
        _handler = handlerContext.Handler;
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

        public bool IsTrayLockVisible { get; set; } = true;
        public bool IsTrayLockEnabled { get; set; } = true;
        public bool IsLocked { get; set; }

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
        public void LockToTrayImmediately() { }
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

        Assert.True(_fakeForm.Visible);
        _autoLockTimer.Verify(t => t.Start(300, It.IsAny<Action>()), Times.Once);
    }

    [Fact]
    public void HideToTray_Visible_StartsHiddenWindowLockingPath()
    {
        _fakeForm.Visible = true;

        _autoLockCoordinator.HideToTray();

        Assert.False(_fakeForm.Visible);
        _autoLockTimer.Verify(t => t.Start(300, It.IsAny<Action>()), Times.Once);
    }

    [Fact]
    public async Task HandleAppDeactivated_WhenVisibleAutoLockGraceStarts_TrayReopenStopsPendingLock()
    {
        Action? backgroundTimeoutCallback = null;
        _autoLockTimer
            .Setup(t => t.Start(300, It.IsAny<Action>()))
            .Callback<int, Action>((_, callback) => backgroundTimeoutCallback = callback);

        _autoLockCoordinator.HandleAppDeactivated();
        backgroundTimeoutCallback!.Invoke();
        await _windowRequestHandler.TryShowWindowAsync();

        Assert.True(_fakeForm.Visible);
        Assert.False(_lockManager.IsLocked);
        _autoLockTimer.Verify(t => t.Stop(), Times.AtLeastOnce);
        _autoLockTimer.Verify(t => t.Start(10, It.IsAny<Action>()), Times.Once);
    }

    [Fact]
    public void HandleAppDeactivated_WhenTimeoutExpiresWhileVisible_HidesToTrayAndStartsGraceTimer()
    {
        Action? backgroundTimeoutCallback = null;
        _autoLockTimer
            .Setup(t => t.Start(300, It.IsAny<Action>()))
            .Callback<int, Action>((_, callback) => backgroundTimeoutCallback = callback);

        _autoLockCoordinator.HandleAppDeactivated();
        backgroundTimeoutCallback!.Invoke();

        Assert.False(_fakeForm.Visible);
        Assert.False(_lockManager.IsLocked);
        _autoLockTimer.Verify(t => t.Start(10, It.IsAny<Action>()), Times.Once);
    }

    [Fact]
    public void HandleAppDeactivated_WhenVisibleAutoLockGraceExpires_LocksAndKeepsWindowHidden()
    {
        Action? backgroundTimeoutCallback = null;
        Action? graceTimeoutCallback = null;
        _autoLockTimer
            .Setup(t => t.Start(300, It.IsAny<Action>()))
            .Callback<int, Action>((_, callback) => backgroundTimeoutCallback = callback);
        _autoLockTimer
            .Setup(t => t.Start(10, It.IsAny<Action>()))
            .Callback<int, Action>((_, callback) => graceTimeoutCallback = callback);

        _autoLockCoordinator.HandleAppDeactivated();
        backgroundTimeoutCallback!.Invoke();
        graceTimeoutCallback!.Invoke();

        Assert.False(_fakeForm.Visible);
        Assert.True(_lockManager.IsLocked);
    }

    [Fact]
    public void HideToTray_WhenAlreadyHidden_UsesDefaultHiddenLockingPath()
    {
        _fakeForm.Visible = false;

        _autoLockCoordinator.HideToTray();

        _autoLockTimer.Verify(t => t.Start(300, It.IsAny<Action>()), Times.Once);
    }

    [Fact]
    public void HandleAppDeactivated_WithZeroTimeoutVisible_StartsGracefulHiddenLockTimer()
    {
        _database.Settings.AutoLockTimeoutMinutes = 0;

        _autoLockCoordinator.HandleAppDeactivated();

        Assert.False(_fakeForm.Visible);
        _autoLockTimer.Verify(t => t.Start(10, It.IsAny<Action>()), Times.Once);
    }

    [Fact]
    public void HandleAppActivated_StopsTimer()
    {
        _autoLockCoordinator.HandleAppDeactivated();

        _autoLockCoordinator.HandleAppActivated();

        _autoLockTimer.Verify(t => t.Stop(), Times.AtLeastOnce);
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
    public void HideToTray_WhenTimeoutIsZero_StartsZeroTimeoutWindowTimerWithGrace()
    {
        _database.Settings.AutoLockTimeoutMinutes = 0;
        _fakeForm.Visible = true;

        _autoLockCoordinator.HideToTray();

        Assert.True(_lockManager.IsLocked);
        Assert.False(_fakeForm.Visible);
        _autoLockTimer.Verify(t => t.Start(It.IsAny<int>(), It.IsAny<Action>()), Times.Never);
    }

    [Fact]
    public void HideToTray_WhenAlreadyHiddenAndTimeoutIsZero_LocksImmediately()
    {
        _database.Settings.AutoLockTimeoutMinutes = 0;
        _fakeForm.Visible = false;

        _autoLockCoordinator.HideToTray();

        Assert.True(_lockManager.IsLocked);
        Assert.False(_fakeForm.Visible);
        _autoLockTimer.Verify(t => t.Start(It.IsAny<int>(), It.IsAny<Action>()), Times.Never);
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
        using var handlerContext = CreateHandlerContext(
            autoLockTimer: new Mock<IAutoLockTimerService>().Object,
            unlockProcessLauncher: unlockProcessLauncher.Object,
            startLocked: true);

        handlerContext.WindowRequestHandler.RequestOperationUnlock();

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
            new Mock<ILoggingService>().Object,
            autoLockTimer.Object,
            new Mock<IUnlockProcessLauncher>().Object,
            new LockStateService(_session),
            new Mock<ICredentialUnlockService>().Object,
            new InlineUiThreadInvoker(a => a()),
            TimeSpan.FromMinutes(5));

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

        Assert.True(shown.Wait(TimeSpan.FromSeconds(5)));
    }

    private HandlerContext CreateHandlerContext(
        IAutoLockTimerService? autoLockTimer = null,
        IUnlockProcessLauncher? unlockProcessLauncher = null,
        bool startLocked = false)
    {
        var lockManager = autoLockTimer == null && unlockProcessLauncher == null ? _lockManager : new LockManager(
            _session,
            new Mock<ILoggingService>().Object,
            autoLockTimer ?? _autoLockTimer.Object,
            unlockProcessLauncher ?? new Mock<IUnlockProcessLauncher>().Object,
            new LockStateService(_session),
            new Mock<ICredentialUnlockService>().Object,
            new InlineUiThreadInvoker(a => a()),
            TimeSpan.FromMinutes(5));
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
        var mockProfilePathResolver = new Mock<IProfilePathResolver>();
        var trayIconManager = new TrayIconManager(
            notifyIcon,
            mockAppIconProvider.Object,
            mockDbProvider.Object,
            new TrayMenuBuilder(
                new SidDisplayNameResolver(mockSidResolver.Object, mockProfilePathResolver.Object),
                new Mock<IIconService>().Object,
                new TrayMenuDiscoveryBuilder(new Mock<IShortcutIconHelper>().Object)),
            new Mock<IInputInjectionBlockerService>().Object,
            new TrayIconOverlayRenderer());
        var foregroundMarkerTrayStatusController = new ForegroundMarkerTrayStatusController(
            notifyIcon,
            trayIconManager,
            Mock.Of<IForegroundPrivilegeMarkerStateSource>(
                source => source.CurrentState == ForegroundPrivilegeMarkerState.Inactive),
            new Mock<ISidNameCacheService>().Object,
            new ApplicationCaptionTextBuilder());

        var accountToolResolver = new AccountToolResolver(new Mock<IProfilePathResolver>().Object);
        var mockLaunchFacade = new Mock<ILaunchFacade>();
        mockLaunchFacade
            .Setup(f => f.LaunchFile(It.IsAny<ProcessLaunchTarget>(), It.IsAny<LaunchIdentity>(), It.IsAny<Func<string, string, bool>?>()))
            .Returns(() => new LaunchExecutionResult(LaunchExecutionStatus.ProcessStarted, null));
        mockLaunchFacade
            .Setup(f => f.LaunchFolderBrowser(It.IsAny<LaunchIdentity>(), It.IsAny<string?>(), It.IsAny<Func<string, string, bool>?>(), It.IsAny<bool>()))
            .Returns(() => new LaunchExecutionResult(LaunchExecutionStatus.ProcessStarted, null));
        var windowsTerminalAccountStateService = new Mock<IWindowsTerminalAccountStateService>();
        windowsTerminalAccountStateService.Setup(service => service.ResolveLaunchTarget(It.IsAny<string>())).Returns("cmd.exe");
        windowsTerminalAccountStateService.Setup(service => service.ResolveLaunchTarget(It.IsAny<AccountLaunchIdentity>())).Returns("cmd.exe");
        var packageInstallService = new PackageInstallService(
            Mock.Of<IPackageInstallLauncher>(),
            Mock.Of<IPackageInstallScriptStore>(),
            accountToolResolver,
            windowsTerminalAccountStateService.Object,
            Mock.Of<IWindowsTerminalDeploymentService>());
        var windowsTerminalLaunchRefreshService = new Mock<IWindowsTerminalLaunchRefreshService>();
        var trayLaunchHandler = new TrayLaunchHandler(
            new Mock<IAppEntryLauncher>().Object,
            mockLaunchFacade.Object,
            new ToolLauncher(
                mockLaunchFacade.Object,
                accountToolResolver,
                windowsTerminalAccountStateService.Object,
                new TerminalLaunchIdentitySelector(Mock.Of<IDatabaseProvider>(), new WindowsTerminalDeploymentPaths(new TestProgramDataKnownPathResolver(Path.GetTempPath()))),
                packageInstallService,
                Mock.Of<IWindowsTerminalDeploymentProgressRunner>(),
                windowsTerminalLaunchRefreshService.Object,
                new Mock<ILaunchFeedbackPresenter>().Object,
                new Mock<ILoggingService>().Object),
            new Mock<ILaunchFeedbackPresenter>().Object,
            new Mock<ISidNameCacheService>().Object,
            new Mock<ILoggingService>().Object);

        var discoveryRefreshManager = new DiscoveryRefreshManager(
            new StartMenuDiscoveryService(new Mock<IProfilePathResolver>().Object, new Mock<IShortcutGateway>().Object, new Mock<ILoggingService>().Object),
            new Mock<ISessionProvider>().Object,
            new Mock<ILoggingService>().Object);

        var handler = new MainFormTrayHandler(
            lockManager,
            trayLaunchHandler,
            notifyIcon,
            trayIconManager,
            new ApplicationCaptionTextBuilder(),
            foregroundMarkerTrayStatusController,
            new Mock<IIdleMonitorService>().Object,
            discoveryRefreshManager,
            _session,
            _licenseService.Object,
            CreateFullModeIdentityFactory(),
            new Mock<IGlobalHotkeyService>().Object,
            windowRequestHandler,
            autoLockCoordinator,
            new TrayIdleMonitorCoordinator(
                new Mock<IIdleMonitorService>().Object,
                _session,
                _licenseService.Object,
                Mock.Of<IApplicationExitService>()));

        lockManager.ShowWindowRequested += windowRequestHandler.ShowAndActivate;
        lockManager.ShowWindowUnlockedRequested += windowRequestHandler.ShowAndActivateForUnlock;
        lockManager.WindowlessUnlockCompleted += autoLockCoordinator.HandleWindowlessUnlock;
        windowRequestHandler.Initialize(_fakeForm);
        autoLockCoordinator.Initialize(_fakeForm);
        windowRequestHandler.SetStartupComplete();

        return new HandlerContext(windowRequestHandler, autoLockCoordinator, handler);
    }

    private static FullModeAccountLaunchIdentityFactory CreateFullModeIdentityFactory()
    {
        var groupQuery = new Mock<ILocalGroupQueryService>();
        groupQuery.Setup(service => service.GetGroupsForUser(It.IsAny<string>()))
            .Returns([new LocalUserAccount("Users", "S-1-5-32-545")]);
        return new FullModeAccountLaunchIdentityFactory(groupQuery.Object);
    }

    private sealed class HandlerContext(
        MainFormWindowRequestHandler windowRequestHandler,
        MainFormBackgroundAutoLockCoordinator autoLockCoordinator,
        MainFormTrayHandler handler)
        : IDisposable
    {
        public MainFormWindowRequestHandler WindowRequestHandler { get; } = windowRequestHandler;
        public MainFormBackgroundAutoLockCoordinator AutoLockCoordinator { get; } = autoLockCoordinator;
        public MainFormTrayHandler Handler { get; } = handler;

        public void Dispose() => Handler.Dispose();
    }

}
