using System.Drawing;
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

public class MainFormTrayHandlerTests
{
    [Fact]
    public void Initialize_WiresTrayMenuActionsThroughMainFormTrayHandler()
    {
        var appEntryLauncher = CreateAppEntryLauncher();
        using var context = CreateContext(appEntryLauncher: appEntryLauncher);
        context.Session.Database.Apps.Add(new AppEntry
        {
            Name = "Paint",
            AccountSid = "S-1-5-21-test",
            ExePath = @"C:\Apps\Paint.exe"
        });

        context.Handler.Initialize(context.Form, context.Form);

        var appItem = context.NotifyIcon.ContextMenuStrip!.Items
            .OfType<ToolStripMenuItem>()
            .Single(item => item.Text == "Paint");
        appItem.PerformClick();

        appEntryLauncher.Verify(x => x.Launch(
            It.Is<AppEntry>(app => app.Name == "Paint"),
            null,
            null,
            It.IsAny<Func<string, string, bool>?>(),
            null), Times.Once);
    }

    [Fact]
    public void LaunchFolderBrowser_UsesShiftToRequestHighestAllowedPrivileges()
    {
        var launchFacade = new Mock<ILaunchFacade>();
        LaunchIdentity? capturedIdentity = null;
        launchFacade
            .Setup(x => x.LaunchFolderBrowser(It.IsAny<LaunchIdentity>(), It.IsAny<string?>(), It.IsAny<Func<string, string, bool>?>(), It.IsAny<bool>()))
            .Callback<LaunchIdentity, string?, Func<string, string, bool>?, bool>((identity, _, _, _) => capturedIdentity = identity)
            .Returns(new LaunchExecutionResult(LaunchExecutionStatus.ProcessStarted, null));
        using var context = CreateContext(launchFacade: launchFacade);

        context.Handler.LaunchFolderBrowser("S-1-5-21-test", shift: true);

        var accountIdentity = Assert.IsType<AccountLaunchIdentity>(capturedIdentity);
        Assert.Equal("S-1-5-21-test", accountIdentity.Sid);
        Assert.Equal(PrivilegeLevel.HighestAllowed, accountIdentity.PrivilegeLevel);
    }

    [Fact]
    public void LaunchFolderBrowser_ShiftForStandardAccountRequestsBasicPrivileges()
    {
        var launchFacade = new Mock<ILaunchFacade>();
        LaunchIdentity? capturedIdentity = null;
        launchFacade
            .Setup(x => x.LaunchFolderBrowser(It.IsAny<LaunchIdentity>(), It.IsAny<string?>(), It.IsAny<Func<string, string, bool>?>(), It.IsAny<bool>()))
            .Callback<LaunchIdentity, string?, Func<string, string, bool>?, bool>((identity, _, _, _) => capturedIdentity = identity)
            .Returns(new LaunchExecutionResult(LaunchExecutionStatus.ProcessStarted, null));
        using var context = CreateContext(
            launchFacade: launchFacade,
            fullModeIdentityFactory: CreateFullModeIdentityFactory());

        context.Handler.LaunchFolderBrowser("S-1-5-21-test", shift: true);

        var accountIdentity = Assert.IsType<AccountLaunchIdentity>(capturedIdentity);
        Assert.Equal("S-1-5-21-test", accountIdentity.Sid);
        Assert.Equal(PrivilegeLevel.Basic, accountIdentity.PrivilegeLevel);
    }

    [Fact]
    public void LaunchDiscoveredApp_UsesAccountIdentityForSelectedSid()
    {
        var launchFacade = new Mock<ILaunchFacade>();
        LaunchIdentity? capturedIdentity = null;
        ProcessLaunchTarget? capturedTarget = null;
        launchFacade
            .Setup(x => x.LaunchFile(It.IsAny<ProcessLaunchTarget>(), It.IsAny<LaunchIdentity>(), It.IsAny<Func<string, string, bool>?>()))
            .Callback<ProcessLaunchTarget, LaunchIdentity, Func<string, string, bool>?>((target, identity, _) =>
            {
                capturedTarget = target;
                capturedIdentity = identity;
            })
            .Returns(new LaunchExecutionResult(LaunchExecutionStatus.ProcessStarted, null));
        using var context = CreateContext(launchFacade: launchFacade);

        context.Handler.LaunchDiscoveredApp(@"C:\Apps\Notepad.exe", "S-1-5-21-test");

        Assert.Equal(@"C:\Apps\Notepad.exe", capturedTarget?.ExePath);
        Assert.False(capturedTarget?.IsPathApproved);
        var accountIdentity = Assert.IsType<AccountLaunchIdentity>(capturedIdentity);
        Assert.Equal("S-1-5-21-test", accountIdentity.Sid);
        Assert.Null(accountIdentity.PrivilegeLevel);
    }

    [Fact]
    public void LaunchTerminal_UsesShiftToRequestHighestAllowedPrivileges()
    {
        var launchFacade = new Mock<ILaunchFacade>();
        LaunchIdentity? capturedIdentity = null;
        ProcessLaunchTarget? capturedTarget = null;
        launchFacade
            .Setup(x => x.LaunchFile(It.IsAny<ProcessLaunchTarget>(), It.IsAny<LaunchIdentity>(), It.IsAny<Func<string, string, bool>?>()))
            .Callback<ProcessLaunchTarget, LaunchIdentity, Func<string, string, bool>?>((target, identity, _) =>
            {
                capturedTarget = target;
                capturedIdentity = identity;
            })
            .Returns(new LaunchExecutionResult(LaunchExecutionStatus.ProcessStarted, null));

        using var context = CreateContext(launchFacade: launchFacade);

        context.Handler.LaunchTerminal("S-1-5-21-test", shift: true);

        Assert.NotNull(capturedTarget);
        var accountIdentity = Assert.IsType<AccountLaunchIdentity>(capturedIdentity);
        Assert.Equal("S-1-5-21-test", accountIdentity.Sid);
        Assert.Equal(PrivilegeLevel.HighestAllowed, accountIdentity.PrivilegeLevel);
    }

    [Fact]
    public void LaunchTerminal_ShiftForStandardAccountRequestsBasicPrivileges()
    {
        var launchFacade = new Mock<ILaunchFacade>();
        LaunchIdentity? capturedIdentity = null;
        ProcessLaunchTarget? capturedTarget = null;
        launchFacade
            .Setup(x => x.LaunchFile(It.IsAny<ProcessLaunchTarget>(), It.IsAny<LaunchIdentity>(), It.IsAny<Func<string, string, bool>?>()))
            .Callback<ProcessLaunchTarget, LaunchIdentity, Func<string, string, bool>?>((target, identity, _) =>
            {
                capturedTarget = target;
                capturedIdentity = identity;
            })
            .Returns(new LaunchExecutionResult(LaunchExecutionStatus.ProcessStarted, null));

        using var context = CreateContext(
            launchFacade: launchFacade,
            fullModeIdentityFactory: CreateFullModeIdentityFactory());

        context.Handler.LaunchTerminal("S-1-5-21-test", shift: true);

        Assert.NotNull(capturedTarget);
        var accountIdentity = Assert.IsType<AccountLaunchIdentity>(capturedIdentity);
        Assert.Equal("S-1-5-21-test", accountIdentity.Sid);
        Assert.Equal(PrivilegeLevel.Basic, accountIdentity.PrivilegeLevel);
    }

    [Fact]
    public void LaunchTerminal_StartsOnlineRefreshAfterLaunchWithoutWaitingForPrelaunchRefresh()
    {
        var calls = new List<string>();
        using var onlineRefreshStarted = new ManualResetEventSlim();
        var launchFacade = new Mock<ILaunchFacade>();
        launchFacade
            .Setup(x => x.LaunchFile(It.IsAny<ProcessLaunchTarget>(), It.IsAny<LaunchIdentity>(), It.IsAny<Func<string, string, bool>?>()))
            .Callback(() => calls.Add("launch"))
            .Returns(new LaunchExecutionResult(LaunchExecutionStatus.ProcessStarted, null));
        var refreshService = new Mock<IWindowsTerminalLaunchRefreshService>();
        refreshService
            .Setup(service => service.TryStartOnlineRefreshAfterTerminalLaunch(It.IsAny<LaunchIdentity>()))
            .Callback(() =>
            {
                calls.Add("online");
                onlineRefreshStarted.Set();
            });

        using var context = CreateContext(
            launchFacade: launchFacade,
            windowsTerminalLaunchRefreshService: refreshService);

        context.Handler.LaunchTerminal("S-1-5-21-test", shift: false);

        Assert.True(onlineRefreshStarted.Wait(TimeSpan.FromSeconds(2)));
        Assert.Equal(["launch", "online"], calls);
    }

    [Fact]
    public void Initialize_Unlicensed_SetsEvaluationTitleButLicenseFreeTrayTooltip()
    {
        using var context = CreateContext(isLicensed: false);
        var captionTextBuilder = new ApplicationCaptionTextBuilder();

        context.Handler.Initialize(context.Form, context.Form);

        Assert.Equal(captionTextBuilder.BuildMainFormTitle(false), context.Form.LastTitle);
        Assert.Equal(captionTextBuilder.BuildBaseTrayTooltip(), context.NotifyIcon.Text);
    }

    [Fact]
    public void Initialize_WithPreExistingActiveMarker_AppliesOverlayDuringInitialization()
    {
        using var context = CreateContext();
        context.StateSource.CurrentState = CreateActiveState(ForegroundPrivilegeMarkerKind.Isolated, Color.Green, "chrome.exe", "S-1-5-21-test");
        context.SidNameCache.Setup(x => x.GetDisplayName("S-1-5-21-test")).Returns("BrowserUser");

        context.Handler.Initialize(context.Form, context.Form);

        Assert.NotNull(context.NotifyIcon.Icon);
        Assert.NotSame(SystemIcons.Application, context.NotifyIcon.Icon);
    }

    [Fact]
    public void UpdateTitleAndTooltip_PreservesActiveMarkerSuffixWhileUpdatingFormTitle()
    {
        using var context = CreateContext(isLicensed: false);
        var captionTextBuilder = new ApplicationCaptionTextBuilder();
        context.StateSource.CurrentState = CreateActiveState(ForegroundPrivilegeMarkerKind.Basic, Color.Blue, "chrome.exe", "S-1-5-21-test");
        context.SidNameCache.Setup(x => x.GetDisplayName("S-1-5-21-test")).Returns("BrowserUser");
        context.Handler.Initialize(context.Form, context.Form);

        context.Handler.UpdateTitleAndTooltip();

        Assert.Equal(captionTextBuilder.BuildMainFormTitle(false), context.Form.LastTitle);
        Assert.Equal(
            captionTextBuilder.BuildForegroundMarkerTrayTooltip("chrome.exe", "BrowserUser", null),
            context.NotifyIcon.Text);
    }

    [Fact]
    public void UpdateTitleAndTooltip_PreservesHiddenForegroundSuffixWhileUpdatingFormTitle()
    {
        using var context = CreateContext(isLicensed: false);
        var captionTextBuilder = new ApplicationCaptionTextBuilder();
        const string currentSid = "S-1-current";
        context.StateSource.CurrentState = ForegroundPrivilegeMarkerState.TooltipOnly(
            new ForegroundPrivilegeMarkerMetadata("powershell.exe", currentSid));
        context.SidNameCache.Setup(x => x.GetDisplayName(currentSid)).Returns("AdminUser");
        context.Handler.Initialize(context.Form, context.Form);

        context.Handler.UpdateTitleAndTooltip();

        Assert.Equal(captionTextBuilder.BuildMainFormTitle(false), context.Form.LastTitle);
        Assert.Equal(
            captionTextBuilder.BuildForegroundMarkerTrayTooltip("powershell.exe", "AdminUser", null),
            context.NotifyIcon.Text);
    }

    [Fact]
    public void UpdateTitleAndTooltip_PreservesHiddenHighIlForegroundSuffixWhileUpdatingFormTitle()
    {
        using var context = CreateContext(isLicensed: false);
        var captionTextBuilder = new ApplicationCaptionTextBuilder();
        context.StateSource.CurrentState = ForegroundPrivilegeMarkerState.TooltipOnly(
            new ForegroundPrivilegeMarkerMetadata("powershell.exe", "S-1-5-21-test"),
            tooltipMode: ForegroundPrivilegeTooltipMode.HighIL);
        context.SidNameCache.Setup(x => x.GetDisplayName("S-1-5-21-test")).Returns("BrowserUser");
        context.Handler.Initialize(context.Form, context.Form);

        context.Handler.UpdateTitleAndTooltip();

        Assert.Equal(captionTextBuilder.BuildMainFormTitle(false), context.Form.LastTitle);
        Assert.Equal(
            captionTextBuilder.BuildForegroundMarkerTrayTooltip("powershell.exe", "BrowserUser", "[HighIL]"),
            context.NotifyIcon.Text);
    }

    [Fact]
    public void UpdateTray_RebuildsActiveTooltipUsingFreshSidDisplayName()
    {
        using var context = CreateContext();
        var captionTextBuilder = new ApplicationCaptionTextBuilder();
        context.StateSource.CurrentState = CreateActiveState(ForegroundPrivilegeMarkerKind.Basic, Color.Blue, "chrome.exe", "S-1-5-21-test");
        context.SidNameCache.SetupSequence(x => x.GetDisplayName("S-1-5-21-test"))
            .Returns("BrowserUser")
            .Returns("RenamedUser");
        context.Handler.Initialize(context.Form, context.Form);

        context.Handler.UpdateTray();

        Assert.Equal(
            captionTextBuilder.BuildForegroundMarkerTrayTooltip("chrome.exe", "RenamedUser", null),
            context.NotifyIcon.Text);
    }

    [Fact]
    public void LicenseStatusChanged_UpdatesTitleButKeepsTrayTooltipLicenseFree()
    {
        using var context = CreateContext(isLicensed: true);
        var captionTextBuilder = new ApplicationCaptionTextBuilder();
        context.StateSource.CurrentState = CreateActiveState(ForegroundPrivilegeMarkerKind.Isolated, Color.Green, "chrome.exe", "S-1-5-21-test");
        context.SidNameCache.Setup(x => x.GetDisplayName("S-1-5-21-test")).Returns("BrowserUser");
        context.Handler.Initialize(context.Form, context.Form);
        context.SetLicensed(false);

        context.LicenseService.Raise(x => x.LicenseStatusChanged += null);

        Assert.Equal(captionTextBuilder.BuildMainFormTitle(false), context.Form.LastTitle);
        Assert.Equal(
            captionTextBuilder.BuildForegroundMarkerTrayTooltip("chrome.exe", "BrowserUser", "[Isolated]"),
            context.NotifyIcon.Text);
    }

    [Fact]
    public void Dispose_DisposesForegroundMarkerController()
    {
        using var context = CreateContext();
        var captionTextBuilder = new ApplicationCaptionTextBuilder();
        context.StateSource.CurrentState = CreateActiveState(ForegroundPrivilegeMarkerKind.Basic, Color.Blue, "chrome.exe", "S-1-5-21-test");
        context.SidNameCache.Setup(x => x.GetDisplayName("S-1-5-21-test")).Returns("BrowserUser");
        context.Handler.Initialize(context.Form, context.Form);
        var titleBeforeDispose = context.Form.LastTitle;

        context.Handler.Dispose();
        context.SidNameCache.Invocations.Clear();

        var exception = Record.Exception(() => context.StateSource.RaiseStateChanged(
            CreateActiveState(ForegroundPrivilegeMarkerKind.LowIL, Color.Red, "cmd.exe", "S-1-5-21-test")));

        Assert.Null(exception);
        context.SidNameCache.Verify(x => x.GetDisplayName(It.IsAny<string>()), Times.Never);
        Assert.Equal(
            captionTextBuilder.BuildMainFormTitle(true),
            titleBeforeDispose);
    }

    private static TestContext CreateContext(
        Mock<ILaunchFacade>? launchFacade = null,
        Mock<IAppEntryLauncher>? appEntryLauncher = null,
        FullModeAccountLaunchIdentityFactory? fullModeIdentityFactory = null,
        Mock<IWindowsTerminalLaunchRefreshService>? windowsTerminalLaunchRefreshService = null,
        bool isLicensed = true)
    {
        launchFacade ??= CreateLaunchFacade();
        appEntryLauncher ??= CreateAppEntryLauncher();

        var session = new SessionContext
        {
            Database = new AppDatabase(),
            CredentialStore = new CredentialStore(),
        }.WithPinDerivedKeyTakingOwnership(TestSecretFactory.Create(32));
        var notifyIcon = new NotifyIcon();
        var markerStateSource = new TestMarkerStateSource();
        var sidNameCache = new Mock<ISidNameCacheService>();
        var appIconProvider = new Mock<IAppIconProvider>();
        appIconProvider.Setup(x => x.GetAppIcon()).Returns(SystemIcons.Application);
        var form = new TestForm();
        var trayIconManager = new TrayIconManager(
            notifyIcon,
            appIconProvider.Object,
            Mock.Of<IDatabaseProvider>(x => x.GetDatabase() == session.Database),
            new TrayMenuBuilder(
                new SidDisplayNameResolver(new Mock<ISidResolver>().Object, new Mock<IProfilePathResolver>().Object),
                new Mock<IIconService>().Object,
                new TrayMenuDiscoveryBuilder(new Mock<IShortcutIconHelper>().Object)),
            new Mock<IInputInjectionBlockerService>().Object,
            new TrayIconOverlayRenderer());
        var licenseService = new Mock<ILicenseService>();
        var licensed = isLicensed;
        licenseService.SetupGet(x => x.IsLicensed).Returns(() => licensed);
        var captionTextBuilder = new ApplicationCaptionTextBuilder();
        var foregroundMarkerTrayStatusController = new ForegroundMarkerTrayStatusController(
            notifyIcon,
            trayIconManager,
            markerStateSource,
            sidNameCache.Object,
            captionTextBuilder);
        var toolResolver = new AccountToolResolver(new Mock<IProfilePathResolver>().Object);
        var windowsTerminalAccountStateService = new Mock<IWindowsTerminalAccountStateService>();
        windowsTerminalAccountStateService.Setup(service => service.ResolveLaunchTarget(It.IsAny<string>())).Returns("cmd.exe");
        windowsTerminalAccountStateService.Setup(service => service.ResolveLaunchTarget(It.IsAny<AccountLaunchIdentity>())).Returns("cmd.exe");
        var feedbackPresenter = new Mock<ILaunchFeedbackPresenter>();
        var packageInstallService = new PackageInstallService(
            Mock.Of<IPackageInstallLauncher>(),
            Mock.Of<IPackageInstallScriptStore>(),
            toolResolver,
            windowsTerminalAccountStateService.Object,
            Mock.Of<IWindowsTerminalDeploymentService>());
        if (windowsTerminalLaunchRefreshService == null)
        {
            windowsTerminalLaunchRefreshService = new Mock<IWindowsTerminalLaunchRefreshService>();
        }
        var trayLaunchHandler = new TrayLaunchHandler(
            appEntryLauncher.Object,
            launchFacade.Object,
            new ToolLauncher(
                launchFacade.Object,
                toolResolver,
                windowsTerminalAccountStateService.Object,
                new TerminalLaunchIdentitySelector(Mock.Of<IDatabaseProvider>(), new WindowsTerminalDeploymentPaths(new TestProgramDataKnownPathResolver(Path.GetTempPath()))),
                packageInstallService,
                Mock.Of<IWindowsTerminalDeploymentProgressRunner>(),
                windowsTerminalLaunchRefreshService.Object,
                feedbackPresenter.Object,
                new Mock<ILoggingService>().Object),
            feedbackPresenter.Object,
            sidNameCache.Object,
            new Mock<ILoggingService>().Object);
        var handler = new MainFormTrayHandler(
            new LockManager(
                session,
                new Mock<ILoggingService>().Object,
                new Mock<IAutoLockTimerService>().Object,
                new Mock<IUnlockProcessLauncher>().Object,
                new LockStateService(session),
                new Mock<ICredentialUnlockService>().Object,
                new InlineUiThreadInvoker(action => action()),
                TimeSpan.FromMinutes(5)),
            trayLaunchHandler,
            notifyIcon,
            trayIconManager,
            captionTextBuilder,
            foregroundMarkerTrayStatusController,
            new Mock<IIdleMonitorService>().Object,
            new DiscoveryRefreshManager(
                new StartMenuDiscoveryService(new Mock<IProfilePathResolver>().Object, new Mock<IShortcutGateway>().Object, new Mock<ILoggingService>().Object),
                new Mock<ISessionProvider>().Object,
                new Mock<ILoggingService>().Object),
            session,
            licenseService.Object,
            fullModeIdentityFactory ?? CreateFullModeIdentityFactory("S-1-5-21-test"),
            new Mock<IGlobalHotkeyService>().Object,
            new MainFormWindowRequestHandler(
                new LockManager(
                    session,
                    new Mock<ILoggingService>().Object,
                    new Mock<IAutoLockTimerService>().Object,
                    new Mock<IUnlockProcessLauncher>().Object,
                    new LockStateService(session),
                    new Mock<ICredentialUnlockService>().Object,
                    new InlineUiThreadInvoker(action => action()),
                    TimeSpan.FromMinutes(5)),
                new Mock<IConfigAvailabilityChecker>().Object,
                new Mock<IShellHelper>().Object,
                licenseService.Object,
                new Mock<ILoggingService>().Object),
            new MainFormBackgroundAutoLockCoordinator(
                new LockManager(
                    session,
                    new Mock<ILoggingService>().Object,
                    new Mock<IAutoLockTimerService>().Object,
                    new Mock<IUnlockProcessLauncher>().Object,
                    new LockStateService(session),
                    new Mock<ICredentialUnlockService>().Object,
                    new InlineUiThreadInvoker(action => action()),
                    TimeSpan.FromMinutes(5)),
                new MainFormWindowRequestHandler(
                    new LockManager(
                        session,
                        new Mock<ILoggingService>().Object,
                        new Mock<IAutoLockTimerService>().Object,
                        new Mock<IUnlockProcessLauncher>().Object,
                        new LockStateService(session),
                        new Mock<ICredentialUnlockService>().Object,
                        new InlineUiThreadInvoker(action => action()),
                        TimeSpan.FromMinutes(5)),
                    new Mock<IConfigAvailabilityChecker>().Object,
                    new Mock<IShellHelper>().Object,
                    licenseService.Object,
                    new Mock<ILoggingService>().Object),
                licenseService.Object),
            new TrayIdleMonitorCoordinator(
                new Mock<IIdleMonitorService>().Object,
                session,
                licenseService.Object,
                Mock.Of<IApplicationExitService>()));

        return new TestContext(
            handler,
            notifyIcon,
            session,
            form,
            markerStateSource,
            sidNameCache,
            licenseService,
            value => licensed = value);
    }

    private static Mock<ILaunchFacade> CreateLaunchFacade()
    {
        var launchFacade = new Mock<ILaunchFacade>();
        launchFacade
            .Setup(x => x.LaunchFile(It.IsAny<ProcessLaunchTarget>(), It.IsAny<LaunchIdentity>(), It.IsAny<Func<string, string, bool>?>()))
            .Returns(new LaunchExecutionResult(LaunchExecutionStatus.ProcessStarted, null));
        launchFacade
            .Setup(x => x.LaunchFolderBrowser(It.IsAny<LaunchIdentity>(), It.IsAny<string?>(), It.IsAny<Func<string, string, bool>?>(), It.IsAny<bool>()))
            .Returns(new LaunchExecutionResult(LaunchExecutionStatus.ProcessStarted, null));
        return launchFacade;
    }

    private static Mock<IAppEntryLauncher> CreateAppEntryLauncher()
    {
        var appEntryLauncher = new Mock<IAppEntryLauncher>();
        appEntryLauncher
            .Setup(x => x.Launch(
                It.IsAny<AppEntry>(),
                null,
                null,
                It.IsAny<Func<string, string, bool>?>(),
                null))
            .Returns(new LaunchExecutionResult(LaunchExecutionStatus.ProcessStarted, null));
        return appEntryLauncher;
    }

    private static ForegroundPrivilegeMarkerState CreateActiveState(
        ForegroundPrivilegeMarkerKind kind,
        Color color,
        string processName,
        string accountSid)
    {
        return ForegroundPrivilegeMarkerState.Active(
            kind,
            color,
            new ForegroundPrivilegeMarkerMetadata(processName, accountSid));
    }

    private static FullModeAccountLaunchIdentityFactory CreateFullModeIdentityFactory(params string[] administratorSids)
    {
        var administratorSet = administratorSids.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var groupQuery = new Mock<ILocalGroupQueryService>();
        groupQuery.Setup(service => service.GetGroupsForUser(It.IsAny<string>()))
            .Returns<string>(sid => administratorSet.Contains(sid)
                ? [new LocalUserAccount("Administrators", "S-1-5-32-544")]
                : [new LocalUserAccount("Users", "S-1-5-32-545")]);
        return new FullModeAccountLaunchIdentityFactory(groupQuery.Object);
    }

    private sealed class TestContext : IDisposable
    {
        public TestContext(
            MainFormTrayHandler handler,
            NotifyIcon notifyIcon,
            SessionContext session,
            TestForm form,
            TestMarkerStateSource stateSource,
            Mock<ISidNameCacheService> sidNameCache,
            Mock<ILicenseService> licenseService,
            Action<bool> setLicensed)
        {
            Handler = handler;
            NotifyIcon = notifyIcon;
            Session = session;
            Form = form;
            StateSource = stateSource;
            SidNameCache = sidNameCache;
            LicenseService = licenseService;
            SetLicensed = setLicensed;
        }

        public MainFormTrayHandler Handler { get; }
        public NotifyIcon NotifyIcon { get; }
        public SessionContext Session { get; }
        public TestForm Form { get; }
        public TestMarkerStateSource StateSource { get; }
        public Mock<ISidNameCacheService> SidNameCache { get; }
        public Mock<ILicenseService> LicenseService { get; }
        public Action<bool> SetLicensed { get; }

        public void Dispose()
        {
            Handler.Dispose();
            NotifyIcon.Dispose();
            Form.Dispose();
            Session.Dispose();
        }
    }

    private sealed class TestForm : Control, IMainFormVisibility, ITrayOwner
    {
        public TestForm()
        {
            CreateControl();
        }

        public bool IsModalActive => false;
        public bool HasOtherWindowsOpen => false;
        public bool IsTrayLockVisible => true;
        public bool IsTrayLockEnabled => true;
        public bool IsLocked => false;
        public string LastTitle { get; private set; } = string.Empty;

        public void BeginInvokeOnUiThread(Action action) => action();
        public void InvokeOnUiThread(Action action) => action();
        public Task TryShowWindowAsync() => Task.CompletedTask;
        public void LockToTrayImmediately() { }

        string IMainFormVisibility.Title { set => LastTitle = value; }
        FormWindowState IMainFormVisibility.WindowState { get; set; }
        bool IMainFormVisibility.ShowInTaskbar { get; set; }
    }

    private sealed class TestMarkerStateSource : IForegroundPrivilegeMarkerStateSource
    {
        public event Action<ForegroundPrivilegeMarkerState>? StateChanged;

        public ForegroundPrivilegeMarkerState CurrentState { get; set; } = ForegroundPrivilegeMarkerState.Inactive;

        public void RaiseStateChanged(ForegroundPrivilegeMarkerState state)
        {
            CurrentState = state;
            StateChanged?.Invoke(state);
        }
    }
}
