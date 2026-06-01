using Moq;
using RunFence.Account;
using RunFence.Account.UI;
using RunFence.Acl;
using RunFence.Apps;
using RunFence.Apps.Shortcuts;
using RunFence.Apps.UI;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Firewall;
using RunFence.Firewall.UI;
using RunFence.Infrastructure;
using RunFence.Launch;
using RunFence.Licensing;
using RunFence.Persistence;
using RunFence.PrefTrans;
using RunFence.Wizard;
using Xunit;

namespace RunFence.Tests;

public class WizardTemplateExecutorTests : IDisposable
{
    private const string TestSid = "S-1-5-21-100-200-300-1001";

    private readonly Mock<IWindowsAccountService> _windowsAccountService = new();
    private readonly Mock<ILocalGroupMutationService> _groupMutation = new();
    private readonly Mock<IAccountRestrictionCoordinator> _restrictionCoordinator = new();
    private readonly Mock<ILicenseService> _licenseService = new();
    private readonly Mock<IAclService> _aclService = new();
    private readonly Mock<IShortcutService> _shortcutService = new();
    private readonly Mock<IBesideTargetShortcutService> _besideTargetShortcutService = new();
    private readonly Mock<IIconService> _iconService = new();
    private readonly Mock<ISidNameCacheService> _sidNameCache = new();
    private readonly Mock<IInteractiveUserDesktopProvider> _desktopProvider = new();
    private readonly Mock<ILoggingService> _log = new();
    private readonly Mock<IShortcutDiscoveryService> _shortcutDiscovery = new();
    private readonly Mock<IWizardSessionSaver> _sessionSaver = new();
    private readonly Mock<IAppEntryIdGenerator> _idGenerator = new();
    private readonly Mock<IUiThreadInvoker> _uiThreadInvoker = new();
    private readonly Mock<IAppStateProvider> _appState = new();
    private readonly Mock<IDatabaseService> _databaseService = new();
    private readonly Mock<IAccountCredentialManager> _credentialManager = new();
    private readonly Mock<ILocalUserProvider> _localUserProvider = new();
    private readonly Mock<ISettingsTransferService> _settingsTransferService = new();
    private readonly AppDatabase _database;
    private readonly SessionContext _session;

    private sealed class TestProgressReporter : IWizardProgressReporter
    {
        public List<string> Errors { get; } = [];
        public List<string> Warnings { get; } = [];
        public List<string> StatusMessages { get; } = [];
        public CancellationToken CancellationToken => CancellationToken.None;
        public void ReportStatus(string message) => StatusMessages.Add(message);
        public void ReportWarning(string message) => Warnings.Add(message);
        public void ReportError(string message) => Errors.Add(message);
    }

    public WizardTemplateExecutorTests()
    {
        _database = new AppDatabase();
        _session = new SessionContext
{
            Database = _database,
            CredentialStore = new CredentialStore(),
        }.WithPinDerivedKeyTakingOwnership(TestSecretFactory.Create(32));

        _appState.Setup(a => a.Database).Returns(_database);
        _uiThreadInvoker.Setup(i => i.Invoke(It.IsAny<Action>())).Callback<Action>(a => a());
        _uiThreadInvoker.Setup(i => i.BeginInvoke(It.IsAny<Action>())).Callback<Action>(a => a());

        _licenseService.Setup(l => l.IsLicensed).Returns(true);
        _licenseService.Setup(l => l.CanAddCredential(It.IsAny<int>())).Returns(true);
        _licenseService.Setup(l => l.CanAddApp(It.IsAny<int>())).Returns(true);
        _restrictionCoordinator
            .Setup(r => r.ApplyRestrictions(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()))
            .Returns(new AccountRestrictionResult(
            [
                new(AccountRestrictionKind.HideLogon, AccountRestrictionStatus.Succeeded, false, null),
                new(AccountRestrictionKind.NetworkLogin, AccountRestrictionStatus.Succeeded, false, null),
                new(AccountRestrictionKind.BackgroundAutorun, AccountRestrictionStatus.Succeeded, false, null),
                new(AccountRestrictionKind.LogonScript, AccountRestrictionStatus.Succeeded, false, null),
                new(AccountRestrictionKind.LsaPolicy, AccountRestrictionStatus.Succeeded, false, null),
            ]));

        _idGenerator.Setup(g => g.GenerateUniqueId(It.IsAny<IEnumerable<string>>()))
            .Returns(() => Guid.NewGuid().ToString("N")[..8]);

        _shortcutDiscovery.Setup(s => s.CreateTraversalCache(It.IsAny<HashSet<string>?>()))
            .Returns(new ShortcutTraversalCache([]));
        _shortcutDiscovery.Setup(s => s.CaptureManagedSids()).Returns([]);
    }

    public void Dispose() => _session.Dispose();

    private WizardTemplateExecutor CreateExecutor()
    {
        var createHandler = new EditAccountDialogCreateHandler(
            _windowsAccountService.Object,
            _groupMutation.Object,
            _restrictionCoordinator.Object,
            _licenseService.Object,
            _uiThreadInvoker.Object,
            _appState.Object,
            _session,
            _databaseService.Object,
            _sidNameCache.Object);

        var firewallSettingsApplier = new Mock<IAccountFirewallSettingsApplier>();
        var portRangeChecker = new DynamicPortRangeChecker(_log.Object, new Mock<IUserConfirmationService>().Object, new StandardNetshCommandRunner());
        var firewallApplyHelper = new FirewallApplyHelper(firewallSettingsApplier.Object, portRangeChecker, _log.Object);
        var profilePathResolver = new Mock<IProfilePathResolver>();
        var launchFacade = new Mock<ILaunchFacade>();
        launchFacade
            .Setup(f => f.LaunchFile(It.IsAny<ProcessLaunchTarget>(), It.IsAny<LaunchIdentity>(), It.IsAny<Func<string, string, bool>?>()))
            .Returns(() => new LaunchExecutionResult(LaunchExecutionStatus.ProcessStarted, null));
        var toolResolver = new AccountToolResolver(profilePathResolver.Object);
        var packageInstallService = new PackageInstallService(
            Mock.Of<IPackageInstallLauncher>(),
            Mock.Of<IPackageInstallScriptStore>(),
            toolResolver,
            Mock.Of<IWindowsTerminalAccountStateService>(),
            Mock.Of<IWindowsTerminalDeploymentService>());
        var databaseProvider = new Mock<IDatabaseProvider>();
        databaseProvider.Setup(d => d.GetDatabase()).Returns(_database);

        var setupHelperFactory = new WizardAccountSetupHelperFactory(
            _credentialManager.Object,
            _localUserProvider.Object,
            _sidNameCache.Object,
            _settingsTransferService.Object,
            firewallApplyHelper,
            packageInstallService,
            databaseProvider.Object,
            _sessionSaver.Object);

        var appEntryBuilder = new AppEntryBuilder(_idGenerator.Object);

        var nonAclEnforcer = AppEntryEnforcementTestFactory.CreateNonAclEnforcer(
            _shortcutService.Object,
            _besideTargetShortcutService.Object,
            _iconService.Object,
            _sidNameCache.Object,
            _desktopProvider.Object,
            new Mock<IInteractiveUserSidResolver>().Object,
            new TestRunFenceLauncherPathProvider(@"C:\RunFence\RunFence.Launcher.exe", exists: true),
            _log.Object);
        var enforcementCoordinator = new AppEntryEnforcementCoordinator(
            _aclService.Object,
            AppEntryEnforcementTestFactory.CreateAclEnforcer(_aclService.Object),
            nonAclEnforcer);

        var credentialCounter = new Mock<IEvaluationCredentialCounter>();
        credentialCounter.Setup(c => c.CountCredentialsExcludingCurrent(It.IsAny<IEnumerable<CredentialEntry>>()))
            .Returns(0);

        var licenseChecker = new WizardLicenseChecker(_licenseService.Object, credentialCounter.Object);

        return new WizardTemplateExecutor(
            createHandler,
            setupHelperFactory,
            appEntryBuilder,
            enforcementCoordinator,
            _shortcutDiscovery.Object,
            _sessionSaver.Object,
            _session,
            licenseChecker);
    }

    private static EditAccountDialogCreateHandler.CreateAccountRequest MakeRequest() =>
        new("TestUser", ProtectedString.FromChars("Pass1!".AsSpan()), ProtectedString.FromChars("Pass1!".AsSpan()), false, [], [], true, true, true, 0);

    [Fact]
    public async Task ExecuteAsync_CredentialLicenseCheckFails_ReturnsEarlyWithoutSave()
    {
        _licenseService.Setup(l => l.CanAddCredential(It.IsAny<int>())).Returns(false);
        _licenseService.Setup(l => l.GetRestrictionMessage(EvaluationFeature.Credentials, It.IsAny<int>()))
            .Returns("Credential limit reached.");

        var flowParams = new WizardStandardFlowParams(
            Request: MakeRequest(),
            SetupOptions: new WizardSetupOptions(
                StoreCredential: true, IsEphemeral: false, PrivilegeLevel: PrivilegeLevel.Isolated,
                FirewallSettings: null, DesktopSettingsPath: null, InstallPackages: null, TrayTerminal: false));

        var progress = new TestProgressReporter();

        await CreateExecutor().ExecuteAsync(flowParams, progress);

        _windowsAccountService.Verify(s => s.CreateLocalUser(It.IsAny<string>(), It.IsAny<ProtectedString>()), Times.Never);
        _sessionSaver.Verify(s => s.SaveAndRefresh(), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_AccountCreationFails_ReportsErrorAndReturnsEarlyWithoutSave()
    {
        _windowsAccountService.Setup(s => s.CreateLocalUser(It.IsAny<string>(), It.IsAny<ProtectedString>()))
            .Throws(new InvalidOperationException("Username already exists."));

        var progress = new TestProgressReporter();

        await CreateExecutor().ExecuteAsync(new WizardStandardFlowParams(Request: MakeRequest(), SetupOptions: null), progress);

        Assert.Contains("Username already exists.", progress.Errors);
        _sessionSaver.Verify(s => s.SaveAndRefresh(), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_AccountCreationPersistsBeforeGroupMutation()
    {
        _windowsAccountService.Setup(s => s.CreateLocalUser("TestUser", It.IsAny<ProtectedString>())).Returns(TestSid);
        var events = new List<string>();
        _databaseService
            .Setup(s => s.SaveConfig(_database, It.IsAny<ISecureSecretSnapshotSource>(), It.IsAny<byte[]>()))
            .Callback(() => events.Add("account-save"));
        _groupMutation
            .Setup(g => g.AddUserToGroups(TestSid, "TestUser", It.IsAny<List<string>>()))
            .Callback(() => events.Add("group-add"));

        var request = new EditAccountDialogCreateHandler.CreateAccountRequest(
            "TestUser",
            ProtectedString.FromChars("Pass1!".AsSpan()),
            ProtectedString.FromChars("Pass1!".AsSpan()),
            false,
            CheckedGroups: [("S-1-5-32-545", "Users")],
            UncheckedGroups: [],
            AllowLogon: true,
            AllowNetworkLogin: true,
            AllowBgAutorun: true,
            CurrentHiddenCount: 0);

        await CreateExecutor().ExecuteAsync(new WizardStandardFlowParams(Request: request, SetupOptions: null), new TestProgressReporter());

        Assert.Equal(["account-save", "group-add"], events);
        Assert.NotNull(_database.GetAccount(TestSid));
    }

    [Fact]
    public async Task ExecuteAsync_RestrictionFailure_ReportsAllRestrictionItemsWithoutDuplicatingFailures()
    {
        _windowsAccountService.Setup(s => s.CreateLocalUser("TestUser", It.IsAny<ProtectedString>())).Returns(TestSid);
        _restrictionCoordinator
            .Setup(r => r.ApplyRestrictions(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()))
            .Returns(new AccountRestrictionResult(
            [
                new(AccountRestrictionKind.HideLogon, AccountRestrictionStatus.Failed, false, "hide failed"),
                new(AccountRestrictionKind.LogonScript, AccountRestrictionStatus.Failed, false, "hide failed"),
                new(AccountRestrictionKind.NetworkLogin, AccountRestrictionStatus.Succeeded, false, null),
                new(AccountRestrictionKind.BackgroundAutorun, AccountRestrictionStatus.Succeeded, false, null),
                new(AccountRestrictionKind.LsaPolicy, AccountRestrictionStatus.Failed, false, "HideLogon: hide failed")
            ]));

        var progress = new TestProgressReporter();

        await CreateExecutor().ExecuteAsync(new WizardStandardFlowParams(Request: MakeRequest(), SetupOptions: null), progress);

        Assert.Contains("HideLogon: hide failed", progress.Errors);
        Assert.Contains("LogonScript: hide failed", progress.Errors);
        Assert.Contains("LsaPolicy: HideLogon: hide failed", progress.Errors);
        Assert.Contains("NetworkLogin: Succeeded", progress.StatusMessages);
        Assert.Contains("BackgroundAutorun: Succeeded", progress.StatusMessages);
        Assert.Equal(1, progress.Errors.Count(e => e == "HideLogon: hide failed"));
    }

    [Fact]
    public async Task ExecuteAsync_PersistsAppIntentBeforeSetupAsyncRuns()
    {
        _windowsAccountService.Setup(s => s.CreateLocalUser("TestUser", It.IsAny<ProtectedString>())).Returns(TestSid);
        _databaseService.Setup(s => s.SaveConfig(_database, It.IsAny<ISecureSecretSnapshotSource>(), It.IsAny<byte[]>()));

        var events = new List<string>();
        _sessionSaver.Setup(s => s.SaveConfig()).Callback(() => events.Add("session-save"));
        _credentialManager
            .Setup(c => c.StoreCreatedUserCredential(TestSid, It.IsAny<ProtectedString>(), _session.CredentialStore, _session.PinDerivedKey))
            .Callback(() =>
            {
                events.Add("store-credential");
                Assert.Single(_database.Apps);
                Assert.Equal(TestSid, _database.Apps[0].AccountSid);
            })
            .Returns(Guid.NewGuid());

        var flowParams = new WizardStandardFlowParams(
            Request: MakeRequest(),
            SetupOptions: new WizardSetupOptions(
                StoreCredential: true,
                IsEphemeral: false,
                PrivilegeLevel: PrivilegeLevel.Isolated,
                FirewallSettings: null,
                DesktopSettingsPath: null,
                InstallPackages: null,
                TrayTerminal: false),
            BuildOptionsFactory: sid =>
            [
                AppEntryBuildOptions.ForWizard(
                    "TestApp",
                    @"C:\Windows\System32\notepad.exe",
                    sid,
                    restrictAcl: false,
                    aclMode: AclMode.Allow,
                    manageShortcuts: false)
            ]);

        await CreateExecutor().ExecuteAsync(flowParams, new TestProgressReporter());

        Assert.True(events.IndexOf("session-save") < events.IndexOf("store-credential"));
    }

    [Fact]
    public async Task ExecuteAsync_PreEnforcementActionThrows_ReportsErrorAndContinuesToSave()
    {
        _windowsAccountService.Setup(s => s.CreateLocalUser("TestUser", It.IsAny<ProtectedString>())).Returns(TestSid);

        var preEnforcementCalled = false;
        var flowParams = new WizardStandardFlowParams(
            Request: MakeRequest(),
            SetupOptions: null,
            PreEnforcementAction: (_, _) =>
            {
                preEnforcementCalled = true;
                throw new InvalidOperationException("Folder grant failed.");
            });

        var progress = new TestProgressReporter();
        await CreateExecutor().ExecuteAsync(flowParams, progress);

        Assert.True(preEnforcementCalled);
        Assert.Contains(progress.Errors, e => e.Contains("Pre-enforcement action") && e.Contains("Folder grant failed."));
        _sessionSaver.Verify(s => s.SaveAndRefresh(), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_PostEnforcementActionThrows_ReportsErrorAndSaves()
    {
        _windowsAccountService.Setup(s => s.CreateLocalUser("TestUser", It.IsAny<ProtectedString>())).Returns(TestSid);

        var postEnforcementCalled = false;
        var flowParams = new WizardStandardFlowParams(
            Request: MakeRequest(),
            SetupOptions: null,
            BuildOptionsFactory: sid => [AppEntryBuildOptions.ForWizard(
                "TestApp", @"C:\Windows\System32\notepad.exe", sid,
                restrictAcl: false, aclMode: AclMode.Allow, manageShortcuts: false)],
            PostEnforcementAction: (_, _) =>
            {
                postEnforcementCalled = true;
                throw new InvalidOperationException("Handler registration failed.");
            });

        var progress = new TestProgressReporter();
        await CreateExecutor().ExecuteAsync(flowParams, progress);

        Assert.True(postEnforcementCalled);
        Assert.Contains(progress.Errors, e => e.Contains("Post-enforcement action") && e.Contains("Handler registration failed."));
        _sessionSaver.Verify(s => s.SaveAndRefresh(), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_AppLicenseCheckFails_ContinuesTryingRemainingAppsAndStillSaves()
    {
        _windowsAccountService.Setup(s => s.CreateLocalUser("TestUser", It.IsAny<ProtectedString>())).Returns(TestSid);
        _licenseService.SetupSequence(l => l.CanAddApp(It.IsAny<int>()))
            .Returns(false)
            .Returns(true);
        _licenseService.Setup(l => l.GetRestrictionMessage(EvaluationFeature.Apps, It.IsAny<int>()))
            .Returns("App limit reached.");

        var flowParams = new WizardStandardFlowParams(
            Request: MakeRequest(),
            SetupOptions: null,
            BuildOptionsFactory: _ =>
            [
                AppEntryBuildOptions.ForWizard(
                    "App1", @"C:\Windows\System32\notepad.exe", TestSid,
                    restrictAcl: false, aclMode: AclMode.Allow, manageShortcuts: false),
                AppEntryBuildOptions.ForWizard(
                    "App2", @"C:\Windows\System32\calc.exe", TestSid,
                    restrictAcl: false, aclMode: AclMode.Allow, manageShortcuts: false)
            ]);

        var progress = new TestProgressReporter();
        await CreateExecutor().ExecuteAsync(flowParams, progress);

        Assert.Single(_database.Apps);
        Assert.Equal("App2", _database.Apps[0].Name);
        Assert.Contains("App limit reached.", progress.Errors);
        _sessionSaver.Verify(s => s.SaveAndRefresh(), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_NoRequest_UsesSidFromFlowParams_AndPreEnforcementReceivesCorrectSid()
    {
        string capturedSid = string.Empty;
        var flowParams = new WizardStandardFlowParams(
            Request: null,
            SetupOptions: null,
            AccountSid: TestSid,
            PreEnforcementAction: (_, sid) =>
            {
                capturedSid = sid;
                return Task.CompletedTask;
            });

        await CreateExecutor().ExecuteAsync(flowParams, new TestProgressReporter());

        Assert.Equal(TestSid, capturedSid);
        _windowsAccountService.Verify(s => s.CreateLocalUser(It.IsAny<string>(), It.IsAny<ProtectedString>()), Times.Never);
        _sessionSaver.Verify(s => s.SaveAndRefresh(), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_HappyPath_AppEntryBuiltAndAddedToDatabase()
    {
        _windowsAccountService.Setup(s => s.CreateLocalUser("TestUser", It.IsAny<ProtectedString>())).Returns(TestSid);

        var flowParams = new WizardStandardFlowParams(
            Request: MakeRequest(),
            SetupOptions: null,
            BuildOptionsFactory: sid => [AppEntryBuildOptions.ForWizard(
                "TestApp", @"C:\Windows\System32\notepad.exe", sid,
                restrictAcl: false, aclMode: AclMode.Allow, manageShortcuts: false)]);

        await CreateExecutor().ExecuteAsync(flowParams, new TestProgressReporter());

        Assert.Single(_database.Apps);
        Assert.Equal("TestApp", _database.Apps[0].Name);
        Assert.Equal(TestSid, _database.Apps[0].AccountSid);
        _sessionSaver.Verify(s => s.SaveAndRefresh(), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_CreateDesktopShortcut_UsesCoordinatorOrderingBeforeAncestorRecompute()
    {
        var events = new List<string>();
        _desktopProvider.Setup(provider => provider.GetDesktopPath()).Returns(@"C:\Users\Test\Desktop");
        _iconService
            .Setup(service => service.CreateBadgedIcon(It.IsAny<AppEntry>(), null))
            .Callback<AppEntry, string?>((app, _) => events.Add($"icon:{app.Name}"))
            .Returns(@"C:\icons\wizard.ico");
        _sidNameCache.Setup(cache => cache.GetDisplayName(TestSid)).Returns(TestSid);
        _shortcutService
            .Setup(service => service.SaveShortcut(It.IsAny<AppEntry>(), It.IsAny<string>()))
            .Callback<AppEntry, string>((app, _) => events.Add($"desktop:{app.Name}"));
        _aclService
            .Setup(service => service.ApplyAcl(It.IsAny<AppEntry>(), It.IsAny<IReadOnlyList<AppEntry>>()))
            .Callback<AppEntry, IReadOnlyList<AppEntry>>((app, _) => events.Add($"acl:{app.Name}"));
        _aclService
            .Setup(service => service.RecomputeAllAncestorAcls(It.IsAny<IReadOnlyList<AppEntry>>()))
            .Callback(() => events.Add("recompute"));

        var flowParams = new WizardStandardFlowParams(
            Request: null,
            SetupOptions: null,
            AccountSid: TestSid,
            CreateDesktopShortcut: true,
            BuildOptionsFactory: sid => [AppEntryBuildOptions.ForWizard(
                "TestApp",
                @"C:\Windows\System32\notepad.exe",
                sid,
                restrictAcl: true,
                aclMode: AclMode.Allow,
                manageShortcuts: false)]);
        var progress = new TestProgressReporter();

        await CreateExecutor().ExecuteAsync(flowParams, progress);

        Assert.Equal(["acl:TestApp", "icon:TestApp", "desktop:TestApp", "recompute"], events);
        Assert.Contains("Applying ACLs...", progress.StatusMessages);
    }

    [Fact]
    public async Task ExecuteAsync_ManagedShortcutApps_CapturesSidsBeforeWorkerAndBuildsTraversalCacheInsideEnforcement()
    {
        var shortcutEvents = new List<string>();
        var managedSids = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { TestSid };
        var callerThreadId = Environment.CurrentManagedThreadId;
        var captureThreadId = 0;
        var traversalThreadId = 0;
        var enforcementThreadId = 0;

        _shortcutDiscovery.Setup(service => service.CaptureManagedSids())
            .Callback(() =>
            {
                captureThreadId = Environment.CurrentManagedThreadId;
                shortcutEvents.Add("capture");
            })
            .Returns(managedSids);
        _shortcutDiscovery.Setup(service => service.CreateTraversalCache(It.IsAny<HashSet<string>?>()))
            .Callback<HashSet<string>?>(capturedSids =>
            {
                traversalThreadId = Environment.CurrentManagedThreadId;
                shortcutEvents.Add("cache");
                Assert.Same(managedSids, capturedSids);
            })
            .Returns(new ShortcutTraversalCache([]));
        _aclService.Setup(service => service.ApplyAcl(It.IsAny<AppEntry>(), It.IsAny<IReadOnlyList<AppEntry>>()))
            .Callback(() => enforcementThreadId = Environment.CurrentManagedThreadId);

        var flowParams = new WizardStandardFlowParams(
            Request: null,
            SetupOptions: null,
            AccountSid: TestSid,
            BuildOptionsFactory: sid => [AppEntryBuildOptions.ForWizard(
                "ManagedShortcutApp",
                @"C:\Windows\System32\notepad.exe",
                sid,
                restrictAcl: true,
                aclMode: AclMode.Allow,
                manageShortcuts: true)]);

        await CreateExecutor().ExecuteAsync(flowParams, new TestProgressReporter());

        Assert.Equal(["capture", "cache"], shortcutEvents);
        Assert.Equal(callerThreadId, captureThreadId);
        Assert.NotEqual(0, traversalThreadId);
        Assert.Equal(traversalThreadId, enforcementThreadId);
        Assert.NotEqual(captureThreadId, traversalThreadId);
    }

    [Fact]
    public async Task ExecuteAsync_ManagedShortcutSidCaptureFailure_ReportsPerAppFailure_ContinuesOtherApps_AndSaves()
    {
        var managedAppPath = @"C:\Windows\System32\notepad.exe";
        var standardAppPath = @"C:\Windows\System32\calc.exe";
        _shortcutDiscovery.Setup(service => service.CaptureManagedSids())
            .Throws(new InvalidOperationException("capture failed"));

        var flowParams = new WizardStandardFlowParams(
            Request: null,
            SetupOptions: null,
            AccountSid: TestSid,
            BuildOptionsFactory: sid =>
            [
                AppEntryBuildOptions.ForWizard(
                    "ManagedShortcutApp",
                    managedAppPath,
                    sid,
                    restrictAcl: true,
                    aclMode: AclMode.Allow,
                    manageShortcuts: true),
                AppEntryBuildOptions.ForWizard(
                    "StandardApp",
                    standardAppPath,
                    sid,
                    restrictAcl: true,
                    aclMode: AclMode.Allow,
                    manageShortcuts: false)
            ]);
        var progress = new TestProgressReporter();

        await CreateExecutor().ExecuteAsync(flowParams, progress);

        Assert.Contains("App entry for ManagedShortcutApp: capture failed", progress.Errors);
        Assert.DoesNotContain(progress.Errors, error => error.Contains("StandardApp", StringComparison.Ordinal));
        Assert.Contains("Done.", progress.StatusMessages);
        _shortcutDiscovery.Verify(service => service.CaptureManagedSids(), Times.Once);
        _shortcutDiscovery.Verify(service => service.CreateTraversalCache(It.IsAny<HashSet<string>?>()), Times.Never);
        _aclService.Verify(
            service => service.ApplyAcl(
                It.Is<AppEntry>(app => app.Name == "StandardApp" && app.ExePath == standardAppPath),
                It.IsAny<IReadOnlyList<AppEntry>>()),
            Times.Once);
        _sessionSaver.Verify(service => service.SaveAndRefresh(), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_NoManagedShortcutApps_DoesNotCaptureSidsOrBuildTraversalCache()
    {
        var flowParams = new WizardStandardFlowParams(
            Request: null,
            SetupOptions: null,
            AccountSid: TestSid,
            BuildOptionsFactory: sid => [AppEntryBuildOptions.ForWizard(
                "StandardApp",
                @"C:\Windows\System32\notepad.exe",
                sid,
                restrictAcl: false,
                aclMode: AclMode.Allow,
                manageShortcuts: false)]);

        await CreateExecutor().ExecuteAsync(flowParams, new TestProgressReporter());

        _shortcutDiscovery.Verify(service => service.CaptureManagedSids(), Times.Never);
        _shortcutDiscovery.Verify(service => service.CreateTraversalCache(It.IsAny<HashSet<string>?>()), Times.Never);
    }
}
