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
    public void LaunchConfiguredApp_UsesExistingTrayLaunchOrchestration()
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
        using var context = CreateContext(appEntryLauncher: appEntryLauncher);

        context.Handler.LaunchConfiguredApp(new AppEntry { Name = "Paint" });

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

    private static TestContext CreateContext(
        Mock<ILaunchFacade>? launchFacade = null,
        Mock<IAppEntryLauncher>? appEntryLauncher = null)
    {
        launchFacade ??= CreateLaunchFacade();
        appEntryLauncher ??= CreateAppEntryLauncher();

        var session = new SessionContext
{
            Database = new AppDatabase(),
            CredentialStore = new CredentialStore(),
        }.WithOwnedPinDerivedKey(TestSecretFactory.Create(32));
        var notifyIcon = new NotifyIcon();
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
            new Mock<IInputInjectionBlockerService>().Object);
        var licenseService = new Mock<ILicenseService>();
        licenseService.SetupGet(x => x.IsLicensed).Returns(true);
        var toolResolver = new AccountToolResolver(new Mock<IProfilePathResolver>().Object);
        var feedbackPresenter = new Mock<ILaunchFeedbackPresenter>();
        var packageInstallService = new PackageInstallService(
            Mock.Of<IPackageInstallLauncher>(),
            Mock.Of<IPackageInstallScriptStore>(),
            toolResolver);
        var trayLaunchHandler = new TrayLaunchHandler(
            appEntryLauncher.Object,
            launchFacade.Object,
            new ToolLauncher(
                launchFacade.Object,
                toolResolver,
                packageInstallService,
                feedbackPresenter.Object,
                new Mock<ILoggingService>().Object),
            feedbackPresenter.Object,
            new Mock<ISidNameCacheService>().Object,
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
            new Mock<IIdleMonitorService>().Object,
            new DiscoveryRefreshManager(
                new StartMenuDiscoveryService(new Mock<IProfilePathResolver>().Object, new Mock<IShortcutComHelper>().Object, new Mock<ILoggingService>().Object),
                new Mock<ISessionProvider>().Object,
                new Mock<ILoggingService>().Object),
            session,
            licenseService.Object,
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
                licenseService.Object));

        return new TestContext(handler, notifyIcon, session, form);
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

    private sealed class TestContext : IDisposable
    {
        public TestContext(
            MainFormTrayHandler handler,
            NotifyIcon notifyIcon,
            SessionContext session,
            TestForm form)
        {
            Handler = handler;
            NotifyIcon = notifyIcon;
            Session = session;
            Form = form;
        }

        public MainFormTrayHandler Handler { get; }
        public NotifyIcon NotifyIcon { get; }
        public SessionContext Session { get; }
        public TestForm Form { get; }

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
        public bool IsModalActive => false;
        public bool HasOtherWindowsOpen => false;
        public bool IsTrayLockVisible => true;
        public bool IsTrayLockEnabled => true;
        public bool IsLocked => false;

        public void BeginInvokeOnUiThread(Action action) => action();
        public void InvokeOnUiThread(Action action) => action();
        public Task TryShowWindowAsync() => Task.CompletedTask;
        public void LockToTrayImmediately() { }

        string IMainFormVisibility.Title { set { } }
        FormWindowState IMainFormVisibility.WindowState { get; set; }
        bool IMainFormVisibility.ShowInTaskbar { get; set; }
    }
}
