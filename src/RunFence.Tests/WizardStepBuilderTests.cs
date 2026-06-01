using System.Linq;
using Moq;
using RunFence.Acl;
using RunFence.Acl.QuickAccess;
using RunFence.Account;
using RunFence.Account.UI;
using RunFence.Apps;
using RunFence.Apps.Shortcuts;
using RunFence.Apps.UI;
using RunFence.Firewall;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Firewall.UI;
using RunFence.Firewall.UI.Forms;
using RunFence.Infrastructure;
using RunFence.Launch;
using RunFence.Licensing;
using RunFence.Launching.Resolution;
using RunFence.Persistence;
using RunFence.Tests.Helpers;
using RunFence.PrefTrans;
using RunFence.UI;
using RunFence.Wizard;
using RunFence.Wizard.Templates;
using RunFence.Wizard.UI.Forms.Steps;
using Xunit;

namespace RunFence.Tests;

public class WizardStepBuilderTests
{
    [Fact]
    public void StandardAppWizardStepBuilder_CreateProjectFoldersStep_ReturnsAllowedPathsStep()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var step = CreateStandardStepBuilder().CreateProjectFoldersStep(_ => { });

            Assert.IsType<AllowedPathsStep>(step);
            Assert.Equal("Project Folders", step.StepTitle);
        });
    }

    [Fact]
    public void AiAgentWizardStepBuilder_CreateToolStep_ReturnsToolStepAndInvokesCommitAction()
    {
        StaTestHelper.RunAsyncOnSta(async () =>
        {
            using var session = CreateSession();
            var commitCalled = false;
            using var step = CreateAiAgentStepBuilder(session).CreateToolStep(
                (_, _) => { },
                progress =>
                {
                    commitCalled = true;
                    return Task.CompletedTask;
                });

            await step.OnCommitBeforeNextAsync(Mock.Of<IWizardProgressReporter>());

            Assert.IsType<AiAgentToolStep>(step);
            Assert.True(commitCalled);
        });
    }

    [Fact]
    public void GamingWizardStepBuilder_CreateLaunchersStep_ReturnsLaunchersStep()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var session = CreateSession();
            using var step = CreateGamingStepBuilder(session).CreateLaunchersStep(_ => { }, () => null);

            Assert.IsType<GamingLaunchersStep>(step);
            Assert.Equal("Game Launchers", step.StepTitle);
        });
    }

    [Fact]
    public void AiAgentTemplate_CreateSteps_ComposesExpectedSequence()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var session = CreateSession();
            var template = CreateAiAgentTemplate(session);

            var steps = template.CreateSteps();
            try
            {
                Assert.Equal(4, steps.Count);
                Assert.IsType<AccountNameStep>(steps[0]);
                Assert.IsType<AllowedPathsStep>(steps[1]);
                Assert.Equal("Project Folders", steps[1].StepTitle);
                Assert.IsType<FirewallOptionsStep>(steps[2]);
                Assert.IsType<AiAgentToolStep>(steps[3]);
            }
            finally
            {
                DisposeSteps(steps.OfType<IDisposable>());
            }
        });
    }

    [Fact]
    public void GamingAccountTemplate_CreateSteps_StartsWithAccountPicker()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var session = CreateSession();
            var template = CreateGamingTemplate(session, CreateDefaultPickerService());

            var steps = template.CreateSteps();
            try
            {
                Assert.IsType<AccountPickerStep>(steps[0]);
            }
            finally
            {
                DisposeSteps(steps.OfType<IDisposable>());
            }
        });
    }

    [Fact]
    public void GamingAccountTemplate_ReplaceFollowingSteps_EmitsCreateAndExistingSequences()
    {
        StaTestHelper.RunOnSta(() =>
        {
            const string existingSid = "S-1-5-21-100-200-300-1001";
            using var session = CreateSession();

            var groupQuery = new Mock<ILocalGroupQueryService>();
            groupQuery
                .Setup(g => g.GetMembersOfGroup(GroupFilterHelper.UsersSid))
                .Returns([new LocalUserAccount("gamer", existingSid)]);
            groupQuery
                .Setup(g => g.GetMembersOfGroup(GroupFilterHelper.AdministratorsSid))
                .Returns([]);

            var localUserProvider = new Mock<ILocalUserProvider>();
            localUserProvider
                .Setup(p => p.GetLocalUserAccounts())
                .Returns([new LocalUserAccount("gamer", existingSid)]);

            var pickerService = new WizardAccountPickerService(
                groupQuery.Object,
                localUserProvider.Object,
                Mock.Of<ISidResolver>(),
                new CredentialFilterHelper(Mock.Of<ISidResolver>()),
                new CredentialDisplayItemFactory(Mock.Of<ISidResolver>(), Mock.Of<IProfilePathResolver>()));

            var template = CreateGamingTemplate(session, pickerService);
            var steps = template.CreateSteps();
            var replacementTitles = new List<IReadOnlyList<string>>();
            var replacementSteps = new List<IReadOnlyList<IDisposable>>();
            try
            {
                var pickerStep = Assert.IsType<AccountPickerStep>(steps[0]);
                var host = new System.Windows.Forms.Form();
                try
                {
                    host.Controls.Add(pickerStep);
                    StaTestHelper.CreateControlTree(host);

                    pickerStep.ReplaceFollowingSteps += (_, newSteps) =>
                    {
                        replacementSteps.Add(newSteps.OfType<IDisposable>().ToList());
                        replacementTitles.Add(newSteps.Select(s => s.StepTitle).ToList());
                    };

                    pickerStep.OnActivated();

                    StaTestHelper.PumpUntil(
                        () =>
                        {
                            var listBox = FindListBox(pickerStep);
                            return listBox.Items.OfType<CreateAccountItem>().Any() &&
                                   listBox.Items.OfType<CredentialDisplayItem>().Any();
                        },
                        timeoutMessage: "Timed out waiting for account picker items to load.");

                    StaTestHelper.PumpUntil(
                        () => replacementTitles.Count >= 1,
                        timeoutMessage: "Timed out waiting for create-account replacement step sequence.");

                    var listBox = FindListBox(pickerStep);
                    listBox.SelectedItem = listBox.Items.OfType<CredentialDisplayItem>().Single();

                    StaTestHelper.PumpUntil(
                        () => replacementTitles.Count >= 2,
                        timeoutMessage: "Timed out waiting for existing-account replacement step sequence.");

                    Assert.Equal(
                        ["Before You Begin", "Account Name", "Game Install Folders", "Game Launchers"],
                        replacementTitles[0].ToArray());
                    Assert.Equal(
                        ["Before You Begin", "Game Install Folders", "Game Launchers"],
                        replacementTitles[1].ToArray());
                }
                finally
                {
                    host.Controls.Remove(pickerStep);
                    host.Dispose();
                }
            }
            finally
            {
                DisposeSteps(replacementSteps.SelectMany(stepList => stepList));
                DisposeSteps(steps.OfType<IDisposable>());
            }
        });
    }

    private static StandardAppWizardStepBuilder CreateStandardStepBuilder() =>
        new(
            Mock.Of<IShortcutDiscoveryService>(),
            Mock.Of<IShortcutIconHelper>(),
            Mock.Of<IOpenFileDialogAdapterFactory>(),
            Mock.Of<IFolderBrowserDialogAdapterFactory>(),
            Mock.Of<IAppDiscoveryDialogService>(),
            Mock.Of<IExecutablePathResolver>());

    private static AiAgentWizardStepBuilder CreateAiAgentStepBuilder(SessionContext session) =>
        new(
            CreateSetupHelperFactory(session),
            Mock.Of<IShortcutDiscoveryService>(),
            Mock.Of<IShortcutIconHelper>(),
            Mock.Of<IOpenFileDialogAdapterFactory>(),
            Mock.Of<IFolderBrowserDialogAdapterFactory>(),
            Mock.Of<IAppDiscoveryDialogService>());

    private static GamingWizardStepBuilder CreateGamingStepBuilder(SessionContext session) =>
        new(
            CreateSetupHelperFactory(session),
            Mock.Of<IShortcutDiscoveryService>(),
            Mock.Of<IShortcutIconHelper>(),
            Mock.Of<IOpenFileDialogAdapterFactory>(),
            Mock.Of<IAppDiscoveryDialogService>());

    private static AiAgentTemplate CreateAiAgentTemplate(SessionContext session)
    {
        return new AiAgentTemplate(
            executor: CreateTemplateExecutor(session),
            setupBuilder: CreateSetupBuilder(session),
            firewallOrchestrator: CreateAiAgentFirewallOrchestrator(session),
            sessionSaver: Mock.Of<IWizardSessionSaver>(),
            session: session,
            stepBuilder: CreateAiAgentStepBuilder(session));
    }

    private static AiAgentFirewallOrchestrator CreateAiAgentFirewallOrchestrator(SessionContext session)
    {
        var databaseProvider = new Mock<IDatabaseProvider>();
        databaseProvider.Setup(p => p.GetDatabase()).Returns(session.Database);

        return new AiAgentFirewallOrchestrator(
            Mock.Of<IFirewallApplyHelper>(),
            Mock.Of<IFirewallDialogFactory>(),
            databaseProvider.Object,
            Mock.Of<ILaunchFacade>(),
            Mock.Of<ILaunchFeedbackPresenter>(),
            CreateToolLauncher(databaseProvider.Object));
    }

    private static ToolLauncher CreateToolLauncher(IDatabaseProvider databaseProvider)
    {
        return new ToolLauncher(
            Mock.Of<ILaunchFacade>(),
            new AccountToolResolver(Mock.Of<IProfilePathResolver>()),
            Mock.Of<IWindowsTerminalAccountStateService>(),
            new TerminalLaunchIdentitySelector(databaseProvider, new WindowsTerminalDeploymentPaths(new TestProgramDataKnownPathResolver(Path.GetTempPath()))),
            Mock.Of<IPackageInstallService>(),
            Mock.Of<IWindowsTerminalDeploymentProgressRunner>(),
            Mock.Of<IWindowsTerminalLaunchRefreshService>(),
            Mock.Of<ILaunchFeedbackPresenter>(),
            Mock.Of<ILoggingService>());
    }

    private static GamingAccountTemplate CreateGamingTemplate(SessionContext session, WizardAccountPickerService pickerService) =>
        new GamingAccountTemplate(
            executor: CreateTemplateExecutor(session),
            setupBuilder: CreateSetupBuilder(session),
            session: session,
            existingAccountPreparationService: CreateExistingAccountPreparationService(),
            pickerStepService: pickerService,
            credentialCollector: CreateCredentialCollector(session),
            stepBuilder: CreateGamingStepBuilder(session));

    private static WizardAccountPickerService CreateDefaultPickerService()
    {
        var sidResolver = Mock.Of<ISidResolver>();

        return new WizardAccountPickerService(
            Mock.Of<ILocalGroupQueryService>(),
            Mock.Of<ILocalUserProvider>(),
            sidResolver,
            new CredentialFilterHelper(sidResolver),
            new CredentialDisplayItemFactory(sidResolver, Mock.Of<IProfilePathResolver>()));
    }

    private static WizardTemplateSetupBuilder CreateSetupBuilder(SessionContext session) =>
        new WizardTemplateSetupBuilder(
            CreateSetupHelperFactory(session),
            new WizardFolderGrantHelper(Mock.Of<IGrantMutatorService>(), Mock.Of<IQuickAccessPinService>()),
            session,
            Mock.Of<IWindowsAccountQueryService>(),
            Mock.Of<IExecutableKindService>());

    private static WizardTemplateExecutor CreateTemplateExecutor(SessionContext session)
    {
        var uiThreadInvoker = new Mock<IUiThreadInvoker>();
        uiThreadInvoker.Setup(i => i.Invoke(It.IsAny<Action>())).Callback<Action>(action => action());

        var createHandler = new EditAccountDialogCreateHandler(
            Mock.Of<IWindowsAccountService>(),
            Mock.Of<ILocalGroupMutationService>(),
            Mock.Of<IAccountRestrictionCoordinator>(),
            Mock.Of<ILicenseService>(),
            uiThreadInvoker.Object,
            Mock.Of<IAppStateProvider>(provider => provider.Database == session.Database),
            session,
            Mock.Of<IMainConfigPersistence>(),
            Mock.Of<ISidNameCacheService>());

        var appEntryBuilder = new AppEntryBuilder(Mock.Of<IAppEntryIdGenerator>());
        var enforcementCoordinator = new AppEntryEnforcementCoordinator(
            Mock.Of<IAclService>(),
            AppEntryEnforcementTestFactory.CreateAclEnforcer(Mock.Of<IAclService>()),
            AppEntryEnforcementTestFactory.CreateNonAclEnforcer(
                Mock.Of<IShortcutService>(),
                Mock.Of<IBesideTargetShortcutService>(),
                Mock.Of<IIconService>(),
                Mock.Of<ISidNameCacheService>()));

        return new WizardTemplateExecutor(
            createHandler,
            CreateSetupHelperFactory(session),
            appEntryBuilder,
            enforcementCoordinator,
            Mock.Of<IShortcutDiscoveryService>(),
            Mock.Of<IWizardSessionSaver>(),
            session,
            new WizardLicenseChecker(Mock.Of<ILicenseService>(), Mock.Of<IEvaluationCredentialCounter>()));
    }

    private static WizardAccountSetupHelperFactory CreateSetupHelperFactory(SessionContext session)
    {
        var databaseProvider = new Mock<IDatabaseProvider>();
        databaseProvider.Setup(p => p.GetDatabase()).Returns(session.Database);
        session.Database.Settings.DefaultDesktopSettingsPath = @"C:\desktop.rfn";

        return new WizardAccountSetupHelperFactory(
            Mock.Of<IAccountCredentialManager>(),
            Mock.Of<ILocalUserProvider>(),
            Mock.Of<ISidNameCacheService>(),
            Mock.Of<ISettingsTransferService>(),
            CreateFirewallApplyHelper(),
            Mock.Of<IPackageInstallService>(),
            databaseProvider.Object,
            Mock.Of<IWizardSessionSaver>());
    }

    private static FirewallApplyHelper CreateFirewallApplyHelper() =>
        new(
            Mock.Of<IAccountFirewallSettingsApplier>(),
            new DynamicPortRangeChecker(
                Mock.Of<ILoggingService>(),
                Mock.Of<IUserConfirmationService>(),
                Mock.Of<INetshCommandRunner>()),
            Mock.Of<ILoggingService>());

    private static WizardCredentialCollector CreateCredentialCollector(SessionContext session)
    {
        var secureDesktopRunner = new Mock<ISecureDesktopRunner>();
        secureDesktopRunner
            .Setup(r => r.Run(It.IsAny<Action>()))
            .Callback<Action>(action => action());

        return new WizardCredentialCollector(
            secureDesktopRunner.Object,
            Mock.Of<ICredentialDialogRunner>(),
            new LambdaSessionProvider(() => session));
    }

    private static GamingExistingAccountPreparationService CreateExistingAccountPreparationService()
    {
        var credentialCounter = new Mock<IEvaluationCredentialCounter>();
        credentialCounter.Setup(c => c.CountCredentialsExcludingCurrent(It.IsAny<IEnumerable<CredentialEntry>>())).Returns(0);
        return new GamingExistingAccountPreparationService(
            new WizardLicenseChecker(Mock.Of<ILicenseService>(), credentialCounter.Object),
            Mock.Of<IAccountCredentialManager>(),
            Mock.Of<IGamingLogonBlockHelper>(),
            Mock.Of<ISidResolver>());
    }

    private static SessionContext CreateSession()
    {
        return new SessionContext
        {
            Database = new AppDatabase(),
            CredentialStore = new CredentialStore()
        }.WithPinDerivedKeyTakingOwnership(TestSecretFactory.Create(32));
    }

    private static System.Windows.Forms.ListBox FindListBox(Control root)
    {
        var pending = new Queue<Control>();
        pending.Enqueue(root);

        while (pending.TryDequeue(out var control))
        {
            if (control is System.Windows.Forms.ListBox listBox)
                return listBox;

            foreach (Control child in control.Controls)
                pending.Enqueue(child);
        }

        throw new Xunit.Sdk.XunitException("Could not find account picker list box.");
    }

    private static void DisposeSteps(IEnumerable<IDisposable> steps)
    {
        foreach (var step in steps)
            step.Dispose();
    }
}
