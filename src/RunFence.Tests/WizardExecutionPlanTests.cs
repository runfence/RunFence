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

public class WizardExecutionPlanTests : IDisposable
{
    private readonly SecureSecret _pinDerivedKey = TestSecretFactory.Create(32);
    private readonly List<SessionContext> _sessions = [];

    public void Dispose()
    {
        foreach (var session in _sessions)
            session.Dispose();

        _pinDerivedKey.Dispose();
    }

    [Fact]
    public void BuildExecutionPlan_SnapshotsBuildOptionsDeterministically()
    {
        var options = new List<AppEntryBuildOptions>
        {
            AppEntryBuildOptions.ForWizard("A", @"C:\a.exe", "S-1", false, AclMode.Allow, false),
            AppEntryBuildOptions.ForWizard("B", @"C:\b.exe", "S-1", false, AclMode.Allow, false)
        };

        var sut = CreateExecutor();
        var plan = sut.BuildExecutionPlan(new WizardStandardFlowParams(
            Request: null,
            SetupOptions: null,
            BuildOptionsFactory: _ => options), "S-1");

        options.Clear();

        Assert.Equal(2, plan.AppBuildOptions.Count);
        Assert.Equal("A", plan.AppBuildOptions[0].Name);
        Assert.Equal("B", plan.AppBuildOptions[1].Name);
    }

    private WizardTemplateExecutor CreateExecutor()
    {
        var session = new SessionContext
{
            Database = new AppDatabase(),
            CredentialStore = new CredentialStore(),
        }.WithClonedPinDerivedKey(_pinDerivedKey);
        _sessions.Add(session);
        var uiThreadInvoker = new Mock<IUiThreadInvoker>();
        uiThreadInvoker.Setup(i => i.Invoke(It.IsAny<Action>())).Callback<Action>(a => a());

        var createHandler = new EditAccountDialogCreateHandler(
            Mock.Of<IWindowsAccountService>(),
            Mock.Of<ILocalGroupMutationService>(),
            Mock.Of<IAccountRestrictionCoordinator>(),
            Mock.Of<ILicenseService>(),
            uiThreadInvoker.Object,
            Mock.Of<IAppStateProvider>(s => s.Database == session.Database),
            session,
            Mock.Of<IDatabaseService>(),
            Mock.Of<ISidNameCacheService>());

        var setupHelperFactory = new WizardAccountSetupHelperFactory(
            Mock.Of<IAccountCredentialManager>(),
            Mock.Of<ILocalUserProvider>(),
            Mock.Of<ISidNameCacheService>(),
            Mock.Of<ISettingsTransferService>(),
            new FirewallApplyHelper(
                Mock.Of<IAccountFirewallSettingsApplier>(),
                new DynamicPortRangeChecker(Mock.Of<ILoggingService>(), Mock.Of<IUserConfirmationService>(), new StandardNetshCommandRunner()),
                Mock.Of<ILoggingService>()),
            new PackageInstallService(
                Mock.Of<IPackageInstallLauncher>(),
                Mock.Of<IPackageInstallScriptStore>(),
                new AccountToolResolver(Mock.Of<IProfilePathResolver>()),
                Mock.Of<IWindowsTerminalAccountStateService>(),
                Mock.Of<IWindowsTerminalDeploymentService>()),
            Mock.Of<IDatabaseProvider>(),
            Mock.Of<IWizardSessionSaver>());

        var appEntryBuilder = new AppEntryBuilder(Mock.Of<IAppEntryIdGenerator>());
        var nonAclEnforcer = AppEntryEnforcementTestFactory.CreateNonAclEnforcer(
            Mock.Of<IShortcutService>(),
            Mock.Of<IBesideTargetShortcutService>(),
            Mock.Of<IIconService>(),
            Mock.Of<ISidNameCacheService>(),
            Mock.Of<IInteractiveUserDesktopProvider>(),
            Mock.Of<IInteractiveUserSidResolver>(),
            new TestRunFenceLauncherPathProvider(@"C:\RunFence\RunFence.Launcher.exe", exists: true),
            Mock.Of<ILoggingService>());
        var enforcementCoordinator = new AppEntryEnforcementCoordinator(
            Mock.Of<IAclService>(),
            AppEntryEnforcementTestFactory.CreateAclEnforcer(Mock.Of<IAclService>()),
            nonAclEnforcer);

        return new WizardTemplateExecutor(
            createHandler,
            setupHelperFactory,
            appEntryBuilder,
            enforcementCoordinator,
            Mock.Of<IShortcutDiscoveryService>(),
            Mock.Of<IWizardSessionSaver>(),
            session,
            new WizardLicenseChecker(
                Mock.Of<ILicenseService>(),
                Mock.Of<IEvaluationCredentialCounter>()));
    }
}
