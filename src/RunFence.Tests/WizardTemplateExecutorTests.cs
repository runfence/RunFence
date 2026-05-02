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

public class WizardTemplateExecutorTests
{
    private const string TestSid = "S-1-5-21-100-200-300-1001";

    private readonly Mock<IWindowsAccountService> _windowsAccountService = new();
    private readonly Mock<ILocalGroupMembershipService> _groupMembership = new();
    private readonly Mock<IAccountLoginRestrictionService> _loginRestriction = new();
    private readonly Mock<IAccountLsaRestrictionService> _lsaRestriction = new();
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
    private readonly Mock<IEvaluationLimitHelper> _evaluationLimitHelper = new();
    private readonly Mock<IAppEntryIdGenerator> _idGenerator = new();
    private readonly AppDatabase _database;
    private readonly ProtectedBuffer _pinKey;
    private readonly SessionContext _session;

    private class TestProgressReporter : IWizardProgressReporter
    {
        public List<string> Errors { get; } = [];
        public List<string> StatusMessages { get; } = [];
        public void ReportStatus(string message) => StatusMessages.Add(message);
        public void ReportError(string message) => Errors.Add(message);
    }

    public WizardTemplateExecutorTests()
    {
        _database = new AppDatabase();
        _pinKey = new ProtectedBuffer(new byte[32], protect: false);
        _session = new SessionContext
        {
            Database = _database,
            CredentialStore = new CredentialStore(),
            PinDerivedKey = _pinKey
        };

        _licenseService.Setup(l => l.IsLicensed).Returns(true);
        _licenseService.Setup(l => l.CanAddCredential(It.IsAny<int>())).Returns(true);
        _licenseService.Setup(l => l.CanAddApp(It.IsAny<int>())).Returns(true);

        _idGenerator.Setup(g => g.GenerateUniqueId(It.IsAny<IEnumerable<string>>()))
            .Returns(() => Guid.NewGuid().ToString("N")[..8]);

        _shortcutDiscovery.Setup(s => s.CreateTraversalCache(It.IsAny<HashSet<string>?>()))
            .Returns(new ShortcutTraversalCache([]));
        _shortcutDiscovery.Setup(s => s.CaptureManagedSids()).Returns([]);
    }

    private WizardTemplateExecutor CreateExecutor()
    {
        var createHandler = new EditAccountDialogCreateHandler(
            _windowsAccountService.Object,
            _groupMembership.Object,
            _loginRestriction.Object,
            _lsaRestriction.Object,
            _licenseService.Object);

        var credentialManager = new Mock<IAccountCredentialManager>();
        var localUserProvider = new Mock<ILocalUserProvider>();
        var settingsTransferService = new Mock<ISettingsTransferService>();
        var firewallSettingsApplier = new Mock<IAccountFirewallSettingsApplier>();
        var portRangeChecker = new DynamicPortRangeChecker(_log.Object, new Mock<IUserConfirmationService>().Object);
        var firewallApplyHelper = new FirewallApplyHelper(firewallSettingsApplier.Object, portRangeChecker, _log.Object);
        var profilePathResolver = new Mock<IProfilePathResolver>();
        var launchFacade = new Mock<ILaunchFacade>();
        var toolResolver = new AccountToolResolver(profilePathResolver.Object);
        var packageInstallService = new PackageInstallService(launchFacade.Object, toolResolver, _log.Object);
        var databaseProvider = new Mock<IDatabaseProvider>();
        databaseProvider.Setup(d => d.GetDatabase()).Returns(_database);

        var setupHelperFactory = new WizardAccountSetupHelperFactory(
            credentialManager.Object,
            localUserProvider.Object,
            _sidNameCache.Object,
            settingsTransferService.Object,
            firewallApplyHelper,
            packageInstallService,
            databaseProvider.Object);

        var appEntryBuilder = new AppEntryBuilder(_idGenerator.Object);

        var enforcementHelper = new AppEntryEnforcementHelper(
            _aclService.Object,
            _shortcutService.Object,
            _besideTargetShortcutService.Object,
            _iconService.Object,
            _sidNameCache.Object,
            _desktopProvider.Object,
            new Mock<IInteractiveUserSidResolver>().Object,
            _log.Object);

        var licenseChecker = new WizardLicenseChecker(_licenseService.Object, _evaluationLimitHelper.Object);

        return new WizardTemplateExecutor(
            createHandler,
            setupHelperFactory,
            appEntryBuilder,
            enforcementHelper,
            _shortcutDiscovery.Object,
            _aclService.Object,
            _sessionSaver.Object,
            _session,
            licenseChecker);
    }

    private static EditAccountDialogCreateHandler.CreateAccountRequest MakeRequest() =>
        new("TestUser", ProtectedString.FromChars("Pass1!".AsSpan()), ProtectedString.FromChars("Pass1!".AsSpan()), false, [], [], true, true, true, 0);

    [Fact]
    public async Task ExecuteAsync_CredentialLicenseCheckFails_ReturnsEarlyWithoutSave()
    {
        // Arrange
        _licenseService.Setup(l => l.CanAddCredential(It.IsAny<int>())).Returns(false);
        _licenseService.Setup(l => l.GetRestrictionMessage(EvaluationFeature.Credentials, It.IsAny<int>()))
            .Returns("Credential limit reached.");

        var flowParams = new WizardStandardFlowParams(
            Request: MakeRequest(),
            SetupOptions: new WizardSetupOptions(
                StoreCredential: true, IsEphemeral: false, PrivilegeLevel: PrivilegeLevel.Basic,
                FirewallSettings: null, DesktopSettingsPath: null, InstallPackages: null, TrayTerminal: false));

        var progress = new TestProgressReporter();

        // Act
        await CreateExecutor().ExecuteAsync(flowParams, progress);

        // Assert — returned early before account creation, no save
        _windowsAccountService.Verify(s => s.CreateLocalUser(It.IsAny<string>(), It.IsAny<ProtectedString>()), Times.Never);
        _sessionSaver.Verify(s => s.SaveAndRefresh(), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_AccountCreationFails_ReportsErrorAndReturnsEarlyWithoutSave()
    {
        // Arrange — CreateLocalUser throws simulating OS failure
        _windowsAccountService.Setup(s => s.CreateLocalUser(It.IsAny<string>(), It.IsAny<ProtectedString>()))
            .Throws(new InvalidOperationException("Username already exists."));

        var flowParams = new WizardStandardFlowParams(Request: MakeRequest(), SetupOptions: null);
        var progress = new TestProgressReporter();

        // Act
        await CreateExecutor().ExecuteAsync(flowParams, progress);

        // Assert — error reported, session not saved
        Assert.Contains("Username already exists.", progress.Errors);
        _sessionSaver.Verify(s => s.SaveAndRefresh(), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_AccountCreationSucceeds_WithNonFatalErrors_ReportsErrorsButContinuesToSave()
    {
        // Arrange — account creation succeeds but group membership fails (non-fatal)
        _windowsAccountService.Setup(s => s.CreateLocalUser("TestUser", It.IsAny<ProtectedString>())).Returns(TestSid);
        _groupMembership.Setup(g => g.AddUserToGroups(TestSid, "TestUser", It.IsAny<List<string>>()))
            .Throws(new UnauthorizedAccessException("Access denied."));

        // Provide a non-empty checked groups list to trigger the group add
        var requestWithGroup = new EditAccountDialogCreateHandler.CreateAccountRequest(
            "TestUser", ProtectedString.FromChars("Pass1!".AsSpan()), ProtectedString.FromChars("Pass1!".AsSpan()), false,
            CheckedGroups: [("S-1-5-32-545", "Users")],
            UncheckedGroups: [],
            AllowLogon: true, AllowNetworkLogin: true, AllowBgAutorun: true, CurrentHiddenCount: 0);

        var flowParams = new WizardStandardFlowParams(Request: requestWithGroup, SetupOptions: null);
        var progress = new TestProgressReporter();

        // Act
        await CreateExecutor().ExecuteAsync(flowParams, progress);

        // Assert — non-fatal error reported but session still saved
        Assert.Contains(progress.Errors, e => e.Contains("Group membership") || e.Contains("Access denied."));
        _sessionSaver.Verify(s => s.SaveAndRefresh(), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_MultipleNonFatalFailures_AllErrorsCollectedAndSessionSaved()
    {
        // Arrange — account creation succeeds; both group membership AND pre-enforcement fail.
        // Both errors must appear in progress.Errors (not just the first one).
        _windowsAccountService.Setup(s => s.CreateLocalUser("TestUser", It.IsAny<ProtectedString>())).Returns(TestSid);
        _groupMembership.Setup(g => g.AddUserToGroups(TestSid, "TestUser", It.IsAny<List<string>>()))
            .Throws(new UnauthorizedAccessException("Group access denied."));

        var requestWithGroup = new EditAccountDialogCreateHandler.CreateAccountRequest(
            "TestUser", ProtectedString.FromChars("Pass1!".AsSpan()), ProtectedString.FromChars("Pass1!".AsSpan()), false,
            CheckedGroups: [("S-1-5-32-545", "Users")],
            UncheckedGroups: [],
            AllowLogon: true, AllowNetworkLogin: true, AllowBgAutorun: true, CurrentHiddenCount: 0);

        var flowParams = new WizardStandardFlowParams(
            Request: requestWithGroup,
            SetupOptions: null,
            PreEnforcementAction: (_, _) => throw new InvalidOperationException("Folder grant failed."));
        var progress = new TestProgressReporter();

        // Act
        await CreateExecutor().ExecuteAsync(flowParams, progress);

        // Assert — both failures reported; session still saved
        Assert.True(progress.Errors.Count >= 2, $"Expected at least 2 errors, got {progress.Errors.Count}: [{string.Join(", ", progress.Errors)}]");
        Assert.Contains(progress.Errors, e => e.Contains("Group access denied.") || e.Contains("Group membership"));
        Assert.Contains(progress.Errors, e => e.Contains("Folder grant failed.") || e.Contains("Pre-enforcement action"));
        _sessionSaver.Verify(s => s.SaveAndRefresh(), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_PreEnforcementActionThrows_ReportsErrorAndContinuesToSave()
    {
        // Arrange
        _windowsAccountService.Setup(s => s.CreateLocalUser("TestUser", It.IsAny<ProtectedString>())).Returns(TestSid);

        var preEnforcementCalled = false;
        var flowParams = new WizardStandardFlowParams(
            Request: MakeRequest(),
            SetupOptions: null,
            AccountSid: null,
            PreEnforcementAction: (_, sid) =>
            {
                preEnforcementCalled = true;
                throw new InvalidOperationException("Folder grant failed.");
            });
        var progress = new TestProgressReporter();

        // Act
        await CreateExecutor().ExecuteAsync(flowParams, progress);

        // Assert — pre-enforcement error reported but session still saved
        Assert.True(preEnforcementCalled);
        Assert.Contains(progress.Errors, e => e.Contains("Pre-enforcement action") && e.Contains("Folder grant failed."));
        _sessionSaver.Verify(s => s.SaveAndRefresh(), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_PostEnforcementActionThrows_ReportsErrorAndSaves()
    {
        // Arrange — account creation succeeds, one app entry built, post-enforcement throws
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

        // Act
        await CreateExecutor().ExecuteAsync(flowParams, progress);

        // Assert — post-enforcement error reported but session still saved
        Assert.True(postEnforcementCalled);
        Assert.Contains(progress.Errors, e => e.Contains("Post-enforcement action") && e.Contains("Handler registration failed."));
        _sessionSaver.Verify(s => s.SaveAndRefresh(), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_AppLicenseCheckFails_StopsAddingAppsButStillSaves()
    {
        // Arrange — account creation succeeds but app license check fails
        _windowsAccountService.Setup(s => s.CreateLocalUser("TestUser", It.IsAny<ProtectedString>())).Returns(TestSid);
        _licenseService.Setup(l => l.CanAddApp(It.IsAny<int>())).Returns(false);
        _licenseService.Setup(l => l.GetRestrictionMessage(EvaluationFeature.Apps, It.IsAny<int>()))
            .Returns("App limit reached.");

        var flowParams = new WizardStandardFlowParams(
            Request: MakeRequest(),
            SetupOptions: null,
            BuildOptionsFactory: _ => [AppEntryBuildOptions.ForWizard(
                "App1", @"C:\Windows\System32\notepad.exe", TestSid,
                restrictAcl: false, aclMode: AclMode.Allow, manageShortcuts: false)]);
        var progress = new TestProgressReporter();

        // Act
        await CreateExecutor().ExecuteAsync(flowParams, progress);

        // Assert — no apps in database (license blocked), but session still saved
        Assert.Empty(_database.Apps);
        _sessionSaver.Verify(s => s.SaveAndRefresh(), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_NoRequest_UsesSidFromFlowParams_AndPreEnforcementReceivesCorrectSid()
    {
        // Arrange — no account creation, SID pre-provided
        var capturedSid = "";
        var flowParams = new WizardStandardFlowParams(
            Request: null,
            SetupOptions: null,
            AccountSid: TestSid,
            PreEnforcementAction: (_, sid) => { capturedSid = sid; return Task.CompletedTask; });
        var progress = new TestProgressReporter();

        // Act
        await CreateExecutor().ExecuteAsync(flowParams, progress);

        // Assert — correct SID passed, no account creation called, session saved
        Assert.Equal(TestSid, capturedSid);
        _windowsAccountService.Verify(s => s.CreateLocalUser(It.IsAny<string>(), It.IsAny<ProtectedString>()), Times.Never);
        _sessionSaver.Verify(s => s.SaveAndRefresh(), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_HappyPath_AccountCreationAndNoApps_CallsSaveAtEnd()
    {
        // Arrange — successful account creation, no apps
        _windowsAccountService.Setup(s => s.CreateLocalUser("TestUser", It.IsAny<ProtectedString>())).Returns(TestSid);

        var flowParams = new WizardStandardFlowParams(Request: MakeRequest(), SetupOptions: null);
        var progress = new TestProgressReporter();

        // Act
        await CreateExecutor().ExecuteAsync(flowParams, progress);

        // Assert — no errors, session saved
        Assert.Empty(progress.Errors);
        _sessionSaver.Verify(s => s.SaveAndRefresh(), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_HappyPath_AppEntryBuiltAndAddedToDatabase()
    {
        // Arrange — account creation succeeds, one app entry to build
        _windowsAccountService.Setup(s => s.CreateLocalUser("TestUser", It.IsAny<ProtectedString>())).Returns(TestSid);

        var flowParams = new WizardStandardFlowParams(
            Request: MakeRequest(),
            SetupOptions: null,
            BuildOptionsFactory: sid => [AppEntryBuildOptions.ForWizard(
                "TestApp", @"C:\Windows\System32\notepad.exe", sid,
                restrictAcl: false, aclMode: AclMode.Allow, manageShortcuts: false)]);
        var progress = new TestProgressReporter();

        // Act
        await CreateExecutor().ExecuteAsync(flowParams, progress);

        // Assert — app added to database and session saved
        Assert.Single(_database.Apps);
        Assert.Equal("TestApp", _database.Apps[0].Name);
        Assert.Equal(TestSid, _database.Apps[0].AccountSid);
        _sessionSaver.Verify(s => s.SaveAndRefresh(), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_PreEnforcementAction_ReceivesCorrectSessionAndSid()
    {
        // Arrange
        _windowsAccountService.Setup(s => s.CreateLocalUser("TestUser", It.IsAny<ProtectedString>())).Returns(TestSid);

        SessionContext? capturedSession = null;
        string capturedSid = "";

        var flowParams = new WizardStandardFlowParams(
            Request: MakeRequest(),
            SetupOptions: null,
            PreEnforcementAction: (session, sid) =>
            {
                capturedSession = session;
                capturedSid = sid;
                return Task.CompletedTask;
            });
        var progress = new TestProgressReporter();

        // Act
        await CreateExecutor().ExecuteAsync(flowParams, progress);

        // Assert
        Assert.Same(_session, capturedSession);
        Assert.Equal(TestSid, capturedSid);
    }
}
