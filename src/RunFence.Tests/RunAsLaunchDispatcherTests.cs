using Moq;
using RunFence.Account;
using RunFence.Apps.Shortcuts;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Launch;
using RunFence.RunAs;
using RunFence.RunAs.UI;
using Xunit;

namespace RunFence.Tests;

/// <summary>
/// Tests for <see cref="RunAsLaunchDispatcher"/> — verifies that container and credential
/// results are dispatched to the correct action: dialog, entry launcher, or direct launcher.
/// CreateAppEntryOnly=true paths are verified by the InvalidOperationException thrown from
/// the fake dialog handler's factory (proving the dialog path was entered).
/// </summary>
public class RunAsLaunchDispatcherTests : IDisposable
{
    private const string UserSid = "S-1-5-21-1000-1000-1000-1001";
    private const string ContainerSid = "S-1-15-2-1-2-3-4-5-6-7";
    private const string ContainerName = "rfn_testcontainer";
    private const string FilePath = @"C:\Apps\test.exe";

    private readonly Mock<IAppStateProvider> _appState = new();
    private readonly Mock<IAppEntryLauncher> _entryLauncher = new();
    private readonly Mock<ILaunchFacade> _directLauncherFacade = new();
    private readonly Mock<ISidResolver> _sidResolver = new();
    private readonly Mock<IRunAsLaunchErrorHandler> _launchErrorHandler = new();
    private readonly Mock<ILoggingService> _log = new();
    private readonly Mock<IShortcutService> _shortcutService = new();

    private readonly AppDatabase _database = new();
    private readonly CredentialStore _credentialStore = new();
    private readonly ProtectedBuffer _pinKey = new(new byte[32], protect: false);
    private readonly FakeRunAsAppEditDialogHandler _fakeDialogHandler;

    public RunAsLaunchDispatcherTests()
    {
        _appState.Setup(a => a.Database).Returns(_database);
        _launchErrorHandler
            .Setup(h => h.RunWithErrorHandling(It.IsAny<Action>(), It.IsAny<string>()))
            .Callback<Action, string>((action, _) => action());
        _fakeDialogHandler = new FakeRunAsAppEditDialogHandler(_pinKey);
    }

    public void Dispose() => _pinKey.Dispose();

    private SessionContext CreateSession() => new()
    {
        Database = _database,
        CredentialStore = _credentialStore,
        PinDerivedKey = _pinKey
    };

    private RunAsAppShortcutCreator CreateShortcutCreator()
    {
        var sidNameCache = new Mock<ISidNameCacheService>();
        var session = CreateSession();
        var sessionProvider = new LambdaSessionProvider(() => session);
        return new RunAsAppShortcutCreator(
            new Mock<IIconService>().Object,
            sidNameCache.Object,
            _shortcutService.Object,
            new Mock<IBesideTargetShortcutService>().Object,
            sessionProvider,
            _log.Object);
    }

    private RunAsDirectLauncher CreateDirectLauncher()
    {
        var sidNameCache = new Mock<ISidNameCacheService>();
        sidNameCache.Setup(s => s.GetDisplayName(It.IsAny<string>())).Returns((string sid) => sid);
        return new RunAsDirectLauncher(
            _appState.Object,
            _directLauncherFacade.Object,
            sidNameCache.Object,
            _sidResolver.Object,
            _launchErrorHandler.Object);
    }

    private RunAsLaunchDispatcher CreateDispatcher()
    {
        var sidNameCache = new Mock<ISidNameCacheService>();
        sidNameCache.Setup(s => s.GetDisplayName(It.IsAny<string>())).Returns((string sid) => sid);
        return new RunAsLaunchDispatcher(
            _fakeDialogHandler,
            _entryLauncher.Object,
            CreateDirectLauncher(),
            sidNameCache.Object,
            _appState.Object,
            _launchErrorHandler.Object,
            CreateShortcutCreator());
    }

    private static RunAsDialogResult MakeContainerResult(
        AppContainerEntry container,
        AppEntry? existingApp = null,
        bool createAppEntryOnly = false)
        => RunAsTestHelpers.MakeContainerResult(container, existingApp, createAppEntryOnly);

    private static RunAsDialogResult MakeCredentialResult(
        CredentialEntry credential,
        AppEntry? existingApp = null,
        bool createAppEntryOnly = false)
        => new(
            Credential: credential,
            SelectedContainer: null,
            PermissionGrant: null,
            CreateAppEntryOnly: createAppEntryOnly,
            PrivilegeLevel: PrivilegeLevel.Basic,
            UpdateOriginalShortcut: false,
            RevertShortcutRequested: false,
            EditExistingApp: null,
            ExistingAppForLaunch: existingApp);

    // ── DispatchContainerResult ────────────────────────────────────────────

    [Fact]
    public void DispatchContainerResult_CreateAppEntryOnly_OpensEditDialog()
    {
        // The fake dialog handler throws InvalidOperationException from dialogFactory(),
        // proving DispatchContainerResult entered the dialog handler path.
        var container = new AppContainerEntry { Name = ContainerName, Sid = ContainerSid };
        using var result = MakeContainerResult(container, createAppEntryOnly: true);
        var dispatcher = CreateDispatcher();

        Assert.Throws<InvalidOperationException>(
            () => dispatcher.DispatchContainerResult(result, FilePath, null, null, false, null));

        _entryLauncher.Verify(l => l.Launch(
            It.IsAny<AppEntry>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<Func<string, string, bool>?>()), Times.Never);
    }

    [Fact]
    public void DispatchContainerResult_ExistingApp_LaunchesViaEntryLauncher()
    {
        var container = new AppContainerEntry { Name = ContainerName, Sid = ContainerSid };
        var existingApp = new AppEntry { Id = "app01", Name = "MyApp", AppContainerName = ContainerName };
        _database.Apps.Add(existingApp);
        using var result = MakeContainerResult(container, existingApp: existingApp);
        var dispatcher = CreateDispatcher();

        dispatcher.DispatchContainerResult(result, FilePath, null, null, false, null);

        _entryLauncher.Verify(l => l.Launch(
            existingApp, null, null, It.IsAny<Func<string, string, bool>?>()), Times.Once);
    }

    [Fact]
    public void DispatchContainerResult_NoExistingApp_LaunchesDirect()
    {
        var container = new AppContainerEntry { Name = ContainerName, Sid = ContainerSid };
        using var result = MakeContainerResult(container);
        var dispatcher = CreateDispatcher();

        dispatcher.DispatchContainerResult(result, FilePath, null, null, false, null);

        _directLauncherFacade.Verify(f => f.LaunchFile(
            It.Is<ProcessLaunchTarget>(t => t.ExePath == FilePath),
            It.IsAny<LaunchIdentity>(),
            It.IsAny<Func<string, string, bool>?>()), Times.Once);
    }

    // ── DispatchCredentialResult ───────────────────────────────────────────

    [Fact]
    public void DispatchCredentialResult_CreateAppEntryOnly_OpensEditDialog()
    {
        var credential = new CredentialEntry { Sid = UserSid };
        using var result = MakeCredentialResult(credential, createAppEntryOnly: true);
        var dispatcher = CreateDispatcher();

        Assert.Throws<InvalidOperationException>(
            () => dispatcher.DispatchCredentialResult(result, FilePath, null, null, false, null));

        _entryLauncher.Verify(l => l.Launch(
            It.IsAny<AppEntry>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<Func<string, string, bool>?>()), Times.Never);
    }

    [Fact]
    public void DispatchCredentialResult_ExistingApp_LaunchesViaEntryLauncher()
    {
        var credential = new CredentialEntry { Sid = UserSid };
        var existingApp = new AppEntry { Id = "app01", Name = "MyApp", AccountSid = UserSid };
        _database.Apps.Add(existingApp);
        using var result = MakeCredentialResult(credential, existingApp: existingApp);
        var dispatcher = CreateDispatcher();

        dispatcher.DispatchCredentialResult(result, FilePath, null, null, false, null);

        _entryLauncher.Verify(l => l.Launch(
            existingApp, null, null, It.IsAny<Func<string, string, bool>?>()), Times.Once);
    }

    [Fact]
    public void DispatchCredentialResult_NoExistingApp_LaunchesDirect()
    {
        var credential = new CredentialEntry { Sid = UserSid };
        using var result = MakeCredentialResult(credential);
        var dispatcher = CreateDispatcher();

        dispatcher.DispatchCredentialResult(result, FilePath, null, null, false, null);

        _directLauncherFacade.Verify(f => f.LaunchFile(
            It.Is<ProcessLaunchTarget>(t => t.ExePath == FilePath),
            It.IsAny<LaunchIdentity>(),
            It.IsAny<Func<string, string, bool>?>()), Times.Once);
    }

}
