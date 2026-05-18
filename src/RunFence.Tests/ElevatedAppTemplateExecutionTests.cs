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
using RunFence.Launching.Resolution;
using RunFence.Licensing;
using RunFence.Persistence;
using RunFence.PrefTrans;
using RunFence.Tests.Helpers;
using RunFence.UI;
using RunFence.Wizard;
using RunFence.Wizard.Templates;
using RunFence.Wizard.UI.Forms.Steps;
using Xunit;

namespace RunFence.Tests;

public class ElevatedAppTemplateExecutionTests : IDisposable
{
    private const string Sid = "S-1-5-21-100-200-300-1001";

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
    private readonly Mock<IAppStateProvider> _appState = new();
    private readonly Mock<IDatabaseService> _databaseService = new();
    private readonly Mock<IAccountCredentialManager> _credentialManager = new();
    private readonly AppDatabase _database;
    private readonly SessionContext _session;

    private sealed class TestProgressReporter : IWizardProgressReporter
    {
        public List<string> Errors { get; } = [];
        public CancellationToken CancellationToken => CancellationToken.None;
        public void ReportStatus(string message)
        {
        }

        public void ReportWarning(string message)
        {
        }

        public void ReportError(string message) => Errors.Add(message);
    }

    public ElevatedAppTemplateExecutionTests()
    {
        _database = new AppDatabase();
        _session = new SessionContext
{
            Database = _database,
            CredentialStore = new CredentialStore(),
        }.WithOwnedPinDerivedKey(TestSecretFactory.Create(32));

        _appState.Setup(a => a.Database).Returns(_database);

        _licenseService.Setup(l => l.IsLicensed).Returns(true);
        _licenseService.Setup(l => l.CanAddCredential(It.IsAny<int>())).Returns(true);
        _licenseService.Setup(l => l.CanAddApp(It.IsAny<int>())).Returns(false);
        _licenseService.Setup(l => l.GetRestrictionMessage(EvaluationFeature.Apps, It.IsAny<int>()))
            .Returns("App limit reached.");

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

        _shortcutDiscovery.Setup(s => s.CreateTraversalCache(It.IsAny<HashSet<string>?>()))
            .Returns(new ShortcutTraversalCache([]));
        _shortcutDiscovery.Setup(s => s.CaptureManagedSids()).Returns([]);
    }

    public void Dispose() => _session.Dispose();

    [Fact]
    public void ExecuteAsync_ExistingAccountAppLicenseFailure_PersistsCollectedCredentialAndContinuesSaveOnlyFlow()
    {
        StaTestHelper.RunAsyncOnSta(async () =>
        {
            using var collectedPassword = ProtectedString.FromChars("WizardPass1!".AsSpan());

            var dialogRunner = new Mock<ICredentialDialogRunner>();
            dialogRunner
                .Setup(r => r.ShowCredentialDialog(
                    It.Is<CredentialEntry>(c => c.Sid == Sid),
                    _session.Database.SidNames))
                .Returns(new CredentialDialogResult(true, collectedPassword));

            _credentialManager
                .Setup(m => m.AddNewCredential(Sid, collectedPassword, _session.CredentialStore, _session.PinDerivedKey))
                .Returns((true, Guid.NewGuid(), (string?)null));

            var template = CreateTemplate(CreateCollector(dialogRunner.Object));
            try
            {
                var progress = new TestProgressReporter();
                var steps = template.CreateSteps();
                var pickerStep = Assert.IsType<AccountPickerStep>(steps[0]);
                var appPathStep = Assert.IsType<AppPathStep>(steps[1]);

                SelectExistingAccount(pickerStep, Sid);
                pickerStep.Collect();
                await pickerStep.OnCommitBeforeNextAsync(progress);

                SetAppPath(appPathStep, Path.Combine(Environment.SystemDirectory, "notepad.exe"), "Notepad");
                Assert.Null(appPathStep.Validate());
                appPathStep.Collect();

                await template.ExecuteAsync(progress);

                _credentialManager.Verify(
                    m => m.AddNewCredential(Sid, collectedPassword, _session.CredentialStore, _session.PinDerivedKey),
                    Times.Once);
                _sessionSaver.Verify(s => s.SaveAndRefresh(), Times.Once);
                Assert.Contains("App limit reached.", progress.Errors);
                Assert.Empty(_database.Apps);
            }
            finally
            {
                template.Cleanup();
            }
        });
    }

    private ElevatedAppTemplate CreateTemplate(WizardCredentialCollector collector)
    {
        var credentialCounter = new Mock<IEvaluationCredentialCounter>();
        credentialCounter
            .Setup(c => c.CountCredentialsExcludingCurrent(It.IsAny<IEnumerable<CredentialEntry>>()))
            .Returns(0);

        return new ElevatedAppTemplate(
            CreateExecutor(new WizardLicenseChecker(_licenseService.Object, credentialCounter.Object)),
            setupHelperFactory: null!,
            credentialManager: _credentialManager.Object,
            pickerStepFactory: CreatePickerFactory(),
            credentialCollector: collector,
            session: _session,
            licenseChecker: new WizardLicenseChecker(_licenseService.Object, credentialCounter.Object),
            discoveryService: Mock.Of<IShortcutDiscoveryService>(),
            iconHelper: Mock.Of<IShortcutIconHelper>(),
            executablePathResolver: CreatePathResolver());
    }

    private WizardTemplateExecutor CreateExecutor(WizardLicenseChecker licenseChecker)
    {
        var createHandler = new EditAccountDialogCreateHandler(
            _windowsAccountService.Object,
            _groupMutation.Object,
            _restrictionCoordinator.Object,
            _licenseService.Object,
            Mock.Of<IUiThreadInvoker>(),
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
            .Returns(new LaunchExecutionResult(LaunchExecutionStatus.ProcessStarted, null));
        var toolResolver = new AccountToolResolver(profilePathResolver.Object);
        var packageInstallService = new PackageInstallService(
            Mock.Of<IPackageInstallLauncher>(),
            Mock.Of<IPackageInstallScriptStore>(),
            toolResolver);
        var databaseProvider = new Mock<IDatabaseProvider>();
        databaseProvider.Setup(d => d.GetDatabase()).Returns(_database);

        var setupHelperFactory = new WizardAccountSetupHelperFactory(
            _credentialManager.Object,
            Mock.Of<ILocalUserProvider>(),
            _sidNameCache.Object,
            Mock.Of<ISettingsTransferService>(),
            firewallApplyHelper,
            packageInstallService,
            databaseProvider.Object,
            _sessionSaver.Object);

        var enforcementHelper = new AppEntryEnforcementHelper(
            _aclService.Object,
            _shortcutService.Object,
            _besideTargetShortcutService.Object,
            _iconService.Object,
            _sidNameCache.Object,
            _desktopProvider.Object,
            new Mock<IInteractiveUserSidResolver>().Object,
            _log.Object);

        return new WizardTemplateExecutor(
            createHandler,
            setupHelperFactory,
            new AppEntryBuilder(Mock.Of<IAppEntryIdGenerator>()),
            enforcementHelper,
            _shortcutDiscovery.Object,
            _aclService.Object,
            _sessionSaver.Object,
            _session,
            licenseChecker);
    }

    private WizardCredentialCollector CreateCollector(ICredentialDialogRunner dialogRunner)
    {
        var secureDesktopRunner = new Mock<ISecureDesktopRunner>();
        secureDesktopRunner
            .Setup(r => r.Run(It.IsAny<Action>()))
            .Callback<Action>(action => action());

        return new WizardCredentialCollector(
            secureDesktopRunner.Object,
            dialogRunner,
            new LambdaSessionProvider(() => _session));
    }

    private static WizardAccountPickerStepFactory CreatePickerFactory()
        => new(
            Mock.Of<ILocalGroupMembershipService>(),
            Mock.Of<ILocalUserProvider>(),
            Mock.Of<ISidResolver>(),
            new CredentialFilterHelper(Mock.Of<ISidResolver>()),
            new CredentialDisplayItemFactory(Mock.Of<ISidResolver>(), Mock.Of<IProfilePathResolver>()));

    private static IExecutablePathResolver CreatePathResolver()
    {
        var resolver = new Mock<IExecutablePathResolver>();
        resolver
            .Setup(r => r.TryResolvePath(It.IsAny<string>(), It.IsAny<ExecutablePathResolutionContext>()))
            .Returns<string, ExecutablePathResolutionContext>((path, _) => path);
        return resolver.Object;
    }

    private static void SelectExistingAccount(AccountPickerStep step, string sid)
    {
        var listBox = step.Controls.OfType<ListBox>().First();
        var displayItem = new CredentialDisplayItemFactory(Mock.Of<ISidResolver>(), Mock.Of<IProfilePathResolver>())
            .Create(new CredentialEntry { Id = Guid.NewGuid(), Sid = sid }, sidNames: null, hasStoredCredential: false);
        listBox.Items.Add(displayItem);
        listBox.SelectedIndex = 0;
    }

    private static void SetAppPath(AppPathStep step, string path, string appName)
    {
        var pathBrowseControl = step.Controls.OfType<AppPathBrowseControl>().First();
        var appNameTextBox = step.Controls.OfType<TextBox>().First();
        pathBrowseControl.PathText = path;
        appNameTextBox.Text = appName;
    }
}
