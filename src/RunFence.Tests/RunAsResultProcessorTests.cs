using System.Security.AccessControl;
using Moq;
using RunFence.Account;
using RunFence.Acl;
using RunFence.Acl.Permissions;
using RunFence.Acl.QuickAccess;
using RunFence.Acl.UI;
using RunFence.Apps.Shortcuts;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Launch;
using RunFence.Persistence;
using RunFence.RunAs;
using RunFence.RunAs.UI;
using RunFence.Security;
using Xunit;

namespace RunFence.Tests;

/// <summary>
/// Tests for <see cref="RunAsResultProcessor"/> — container and credential result handling,
/// focusing on permission grant → save → launch flows and the existing-entry reuse path.
/// CreateAppEntryOnly=true paths call into <see cref="RunAsAppEditDialogHandler"/> which
/// opens WinForms dialogs and is not suitable for automated tests.
/// </summary>
public class RunAsResultProcessorTests : IDisposable
{
    private const string UserSid = "S-1-5-21-1000-1000-1000-1001";
    private const string ContainerName = "rfn_testcontainer";
    private const string ContainerSid = "S-1-15-2-1-2-3-4-5-6-7";
    private const string FilePath = @"C:\Apps\test.exe";

    private readonly Mock<IAppStateProvider> _appState = new();
    private readonly Mock<ILoggingService> _log = new();
    private readonly Mock<IDatabaseService> _databaseService = new();
    private readonly Mock<IShortcutService> _shortcutService = new();
    private readonly Mock<IIconService> _iconService = new();
    private readonly Mock<IAppEntryLauncher> _launchOrchestrator = new();
    private readonly Mock<IPathGrantService> _pathGrantService = new();

    private readonly Mock<ICredentialEncryptionService> _encryptionService = new();
    private readonly Mock<ISidResolver> _sidResolver = new();
    private readonly Mock<IRunAsLaunchErrorHandler> _launchErrorHandler = new();
    private readonly Mock<ILaunchFacade> _directLauncherFacade = new();
    private readonly AppDatabase _database = new();
    private readonly CredentialStore _credentialStore = new();
    private readonly ProtectedBuffer _pinKey = new(new byte[32], protect: false);
    private readonly FakeRunAsAppEditDialogHandler _fakeDialogHandler;

    public RunAsResultProcessorTests()
    {
        _fakeDialogHandler = new FakeRunAsAppEditDialogHandler(_pinKey);
        _appState.Setup(c => c.Database).Returns(_database);
        _launchErrorHandler
            .Setup(h => h.RunWithErrorHandling(It.IsAny<Action>(), It.IsAny<string>()))
            .Callback<Action, string>((action, _) => action());
    }

    public void Dispose()
    {
        _pinKey.Dispose();
    }

    private SessionContext CreateSession() => new()
    {
        Database = _database,
        CredentialStore = _credentialStore,
        PinDerivedKey = _pinKey
    };

    private RunAsAppShortcutCreator CreateShortcutCreator()
    {
        var session = CreateSession();
        var sessionProvider = new LambdaSessionProvider(() => session);
        return new RunAsAppShortcutCreator(
            _iconService.Object,
            new Mock<ISidNameCacheService>().Object,
            _shortcutService.Object,
            new Mock<IBesideTargetShortcutService>().Object,
            sessionProvider,
            _log.Object);
    }

    private RunAsCredentialPersister CreateCredentialPersister()
        => new(
            _appState.Object,
            CreateSession(),
            _encryptionService.Object,
            _databaseService.Object,
            _log.Object);

    private RunAsDosProtection CreateDosProtection()
    {
        var stopwatch = new Mock<IStopwatchProvider>();
        return new RunAsDosProtection(stopwatch.Object);
    }

    private RunAsDirectLauncher CreateDirectLauncher()
    {
        var sidNameCache = new Mock<ISidNameCacheService>();
        sidNameCache.Setup(s => s.GetDisplayName(It.IsAny<string>())).Returns((string sid) => sid);
        return new(_appState.Object, _directLauncherFacade.Object, sidNameCache.Object,
            _sidResolver.Object, _launchErrorHandler.Object);
    }

    private RunAsPermissionApplier CreatePermissionApplier()
        => new(
            _pathGrantService.Object,
            _databaseService.Object,
            CreateSession(),
            _appState.Object,
            _log.Object,
            new Mock<IQuickAccessPinService>().Object);

    private RunAsLaunchDispatcher CreateLaunchDispatcher(RunAsDirectLauncher directLauncher)
    {
        var sidNameCache = new Mock<ISidNameCacheService>();
        sidNameCache.Setup(s => s.GetDisplayName(It.IsAny<string>())).Returns((string sid) => sid);
        return new RunAsLaunchDispatcher(
            _fakeDialogHandler,
            _launchOrchestrator.Object,
            directLauncher,
            sidNameCache.Object,
            _appState.Object,
            _launchErrorHandler.Object,
            CreateShortcutCreator());
    }

    private RunAsResultProcessor CreateProcessor()
    {
        // We use a real RunAsCredentialPersister backed by the mock IDatabaseService.
        // All test cases use CreateAppEntryOnly=false so _fakeDialogHandler methods are never called.
        var persister = CreateCredentialPersister();
        var dosProtection = CreateDosProtection();
        var directLauncher = CreateDirectLauncher();

        return new RunAsResultProcessor(
            CreatePermissionApplier(),
            CreateLaunchDispatcher(directLauncher),
            persister,
            dosProtection,
            _shortcutService.Object,
            _log.Object);
    }

    private static RunAsDialogResult MakeCredentialResult(
        CredentialEntry credential,
        AppEntry? existingApp = null,
        AncestorPermissionResult? permissionGrant = null)
        => new(
            Credential: credential,
            SelectedContainer: null,
            PermissionGrant: permissionGrant,
            CreateAppEntryOnly: false,
            PrivilegeLevel: PrivilegeLevel.Basic,
            UpdateOriginalShortcut: false,
            RevertShortcutRequested: false,
            EditExistingApp: null,
            ExistingAppForLaunch: existingApp);

    private static RunAsDialogResult MakeContainerResult(
        AppContainerEntry container,
        AppEntry? existingApp = null,
        AncestorPermissionResult? permissionGrant = null)
        => RunAsTestHelpers.MakeContainerResult(container, existingApp, permissionGrant: permissionGrant);

    // ── ProcessCredentialResult — permission grant → save → launch ──────

    [Fact]
    public void ProcessCredentialResult_NoPermissionGrant_SkipsGrantCall()
    {
        // Pre-set last-used SID so the credential persister detects no change and skips saving,
        // allowing this test to verify that no permission grant path runs.
        _database.Settings.LastUsedRunAsAccountSid = UserSid;
        var credential = new CredentialEntry { Sid = UserSid };
        using var result = MakeCredentialResult(credential);
        var processor = CreateProcessor();

        processor.ProcessCredentialResult(result, FilePath, null, null, false, null);

        _pathGrantService.Verify(
            p => p.EnsureAccess(It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<FileSystemRights>(), It.IsAny<Func<string, string, bool>?>(), It.IsAny<bool>()),
            Times.Never);
        _databaseService.Verify(d => d.SaveConfig(It.IsAny<AppDatabase>(),
            It.IsAny<byte[]>(), It.IsAny<byte[]>()), Times.Never);
    }

    [Fact]
    public void ProcessCredentialResult_WithPermissionGrant_GrantsAndSavesWhenGranted()
    {
        var credential = new CredentialEntry { Sid = UserSid };
        var permissionGrant = new AncestorPermissionResult(@"C:\Data", FileSystemRights.ReadAndExecute);
        _pathGrantService
            .Setup(p => p.EnsureAccess(UserSid, @"C:\Data", FileSystemRights.ReadAndExecute, null, false))
            .Returns(new GrantOperationResult(GrantAdded: true, TraverseAdded: false, DatabaseModified: true));

        using var result = MakeCredentialResult(credential, permissionGrant: permissionGrant);
        var processor = CreateProcessor();

        processor.ProcessCredentialResult(result, FilePath, null, null, false, null);

        _pathGrantService.Verify(
            p => p.EnsureAccess(UserSid, @"C:\Data", FileSystemRights.ReadAndExecute, null, false),
            Times.Once);
        // SaveConfig may be called multiple times (permission grant save + last-used account save)
        _databaseService.Verify(
            d => d.SaveConfig(_database, It.IsAny<byte[]>(), It.IsAny<byte[]>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public void ProcessCredentialResult_WithPermissionGrant_NoGrantMade_PermissionGrantNotSaved()
    {
        // Pre-set last-used SID so the credential persister detects no change and skips saving,
        // isolating the assertion to the permission-grant save path only.
        _database.Settings.LastUsedRunAsAccountSid = UserSid;
        var credential = new CredentialEntry { Sid = UserSid };
        var permissionGrant = new AncestorPermissionResult(@"C:\Data", FileSystemRights.ReadAndExecute);
        _pathGrantService
            .Setup(p => p.EnsureAccess(It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<FileSystemRights>(), It.IsAny<Func<string, string, bool>?>(), It.IsAny<bool>()))
            .Returns(new GrantOperationResult());

        using var result = MakeCredentialResult(credential, permissionGrant: permissionGrant);
        var processor = CreateProcessor();

        processor.ProcessCredentialResult(result, FilePath, null, null, false, null);

        // EnsureAccess returned false → no permission-grant save
        _databaseService.Verify(d => d.SaveConfig(It.IsAny<AppDatabase>(),
            It.IsAny<byte[]>(), It.IsAny<byte[]>()), Times.Never);
    }

    [Fact]
    public void ProcessCredentialResult_ExistingApp_LaunchesViaOrchestratorWithArguments()
    {
        var credential = new CredentialEntry { Sid = UserSid };
        var existingApp = new AppEntry { Id = "app01", Name = "MyApp", AccountSid = UserSid };
        _database.Apps.Add(existingApp);
        using var result = MakeCredentialResult(credential, existingApp: existingApp);
        var processor = CreateProcessor();

        processor.ProcessCredentialResult(result, FilePath, "--arg", null, false, null);

        _launchOrchestrator.Verify(o => o.Launch(existingApp, "--arg", null, It.IsAny<Func<string, string, bool>?>()), Times.Once);
    }

    [Fact]
    public void ProcessCredentialResult_NoExistingApp_LaunchesDirectly()
    {
        var credential = new CredentialEntry { Sid = UserSid };
        using var result = MakeCredentialResult(credential);
        var processor = CreateProcessor();

        processor.ProcessCredentialResult(result, FilePath, null, null, false, null);

        _directLauncherFacade.Verify(f => f.LaunchFile(It.Is<ProcessLaunchTarget>(t => t.ExePath == FilePath && t.Arguments == null),
            It.Is<AccountLaunchIdentity>(a => a.Sid == UserSid), It.IsAny<Func<string, string, bool>?>()), Times.Once);
    }

    // ── ProcessContainerResult — basic flow ─────────────────────────────

    [Fact]
    public void ProcessContainerResult_WithPermissionGrantAndContainer_GrantsAccess()
    {
        var container = new AppContainerEntry { Name = ContainerName, Sid = ContainerSid };
        var permissionGrant = new AncestorPermissionResult(@"C:\Data", FileSystemRights.ReadAndExecute);
        _pathGrantService
            .Setup(p => p.EnsureAccess(ContainerSid, @"C:\Data", FileSystemRights.ReadAndExecute, null, false))
            .Returns(new GrantOperationResult(GrantAdded: true, TraverseAdded: false, DatabaseModified: true));

        using var result = MakeContainerResult(container, permissionGrant: permissionGrant);
        var processor = CreateProcessor();

        processor.ProcessContainerResult(result, FilePath, null, null, false, null);

        _pathGrantService.Verify(
            p => p.EnsureAccess(ContainerSid, @"C:\Data", FileSystemRights.ReadAndExecute, null, false),
            Times.Once);
        // SaveConfig must be called at least once for the permission grant save
        _databaseService.Verify(d => d.SaveConfig(_database,
            It.IsAny<byte[]>(), It.IsAny<byte[]>()), Times.AtLeastOnce);
    }

    [Fact]
    public void ProcessContainerResult_ExistingApp_LaunchesViaOrchestrator()
    {
        var container = new AppContainerEntry { Name = ContainerName, Sid = ContainerSid };
        var existingApp = new AppEntry
        {
            Id = "app01", Name = "MyApp",
            AppContainerName = ContainerName, AccountSid = string.Empty
        };
        _database.Apps.Add(existingApp);
        using var result = MakeContainerResult(container, existingApp: existingApp);
        var processor = CreateProcessor();

        processor.ProcessContainerResult(result, FilePath, null, null, false, null);

        _launchOrchestrator.Verify(o => o.Launch(existingApp, null, null, It.IsAny<Func<string, string, bool>?>()), Times.Once);
    }

    // ── ProcessShortcutRevert ───────────────────────────────────────────

    [Fact]
    public void ProcessShortcutRevert_DelegatesToShortcutService()
    {
        var app = new AppEntry { Id = "app01", Name = "MyApp", AccountSid = UserSid };
        var lnkPath = @"C:\Users\Public\Desktop\MyApp.lnk";
        var processor = CreateProcessor();

        processor.ProcessShortcutRevert(lnkPath, app);

        _shortcutService.Verify(s => s.RevertSingleShortcut(lnkPath, app), Times.Once);
    }

    [Fact]
    public void ProcessShortcutRevert_ShortcutServiceThrows_PropagatesException()
    {
        // ProcessShortcutRevert does not catch exceptions — the caller (RunAsFlowHandler)
        // is responsible for catching and displaying the error dialog on the UI thread.
        var app = new AppEntry { Id = "app01", Name = "MyApp", AccountSid = UserSid };
        var lnkPath = @"C:\Users\Public\Desktop\MyApp.lnk";
        _shortcutService.Setup(s => s.RevertSingleShortcut(It.IsAny<string>(), It.IsAny<AppEntry>()))
            .Throws(new IOException("Access denied"));
        var processor = CreateProcessor();

        Assert.Throws<IOException>(() => processor.ProcessShortcutRevert(lnkPath, app));
    }

}
