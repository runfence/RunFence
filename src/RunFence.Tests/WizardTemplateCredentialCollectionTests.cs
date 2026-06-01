using Moq;
using RunFence.Account;
using RunFence.Account.UI;
using RunFence.Apps.Shortcuts;
using RunFence.Apps.UI;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Licensing;
using RunFence.Launching.Resolution;
using RunFence.Persistence;
using RunFence.Firewall.UI;
using RunFence.PrefTrans;
using RunFence.Acl;
using RunFence.Acl.QuickAccess;
using RunFence.Tests.Helpers;
using RunFence.UI;
using RunFence.Wizard;
using RunFence.Wizard.Templates;
using RunFence.Wizard.UI.Forms.Steps;
using Xunit;

namespace RunFence.Tests;

public class WizardTemplateCredentialCollectionTests
{
    private const string Sid = "S-1-5-21-100-200-300-1001";

    [Fact]
    public void ElevatedAppTemplate_ExistingAccountCommit_CollectsCredentialThroughInjectedCollector()
    {
        StaTestHelper.RunAsyncOnSta(async () =>
        {
            using var session = CreateSession();
            var password = ProtectedString.FromChars("WizardPass1!".AsSpan());
            var dialogRunner = new Mock<ICredentialDialogRunner>();
            dialogRunner
                .Setup(r => r.ShowCredentialDialog(
                    It.Is<CredentialEntry>(c => c.Sid == Sid),
                    session.Database.SidNames))
                .Returns(new CredentialDialogResult(true, password));

            var template = CreateElevatedTemplate(
                session,
                CreateCollector(session, dialogRunner.Object));
            try
            {
                var pickerStep = Assert.IsType<AccountPickerStep>(template.CreateSteps().First());
                SelectExistingAccount(pickerStep, Sid);

                pickerStep.Collect();
                await pickerStep.OnCommitBeforeNextAsync(Mock.Of<IWizardProgressReporter>());

                dialogRunner.Verify(r => r.ShowCredentialDialog(
                    It.Is<CredentialEntry>(c => c.Sid == Sid),
                    session.Database.SidNames), Times.Once);
                Assert.True(password.Length > 0);
            }
            finally
            {
                template.Cleanup();
            }

            Assert.Throws<ObjectDisposedException>(() => password.Length);
        });
    }

    [Fact]
    public void GamingAccountTemplate_ExistingAccountCommit_CollectsCredentialThroughInjectedCollector()
    {
        StaTestHelper.RunAsyncOnSta(async () =>
        {
            using var session = CreateSession();
            var password = ProtectedString.FromChars("WizardPass1!".AsSpan());
            var dialogRunner = new Mock<ICredentialDialogRunner>();
            dialogRunner
                .Setup(r => r.ShowCredentialDialog(
                    It.Is<CredentialEntry>(c => c.Sid == Sid),
                    session.Database.SidNames))
                .Returns(new CredentialDialogResult(true, password));

            var template = CreateGamingTemplate(
                session,
                CreateCollector(session, dialogRunner.Object));
            try
            {
                var pickerStep = Assert.IsType<AccountPickerStep>(template.CreateSteps().First());
                SelectExistingAccount(pickerStep, Sid);

                pickerStep.Collect();
                await pickerStep.OnCommitBeforeNextAsync(Mock.Of<IWizardProgressReporter>());

                dialogRunner.Verify(r => r.ShowCredentialDialog(
                    It.Is<CredentialEntry>(c => c.Sid == Sid),
                    session.Database.SidNames), Times.Once);
                Assert.True(password.Length > 0);
            }
            finally
            {
                template.Cleanup();
            }

            Assert.Throws<ObjectDisposedException>(() => password.Length);
        });
    }

    private static ElevatedAppTemplate CreateElevatedTemplate(SessionContext session, WizardCredentialCollector collector)
        => new(
            executor: null!,
            setupHelperFactory: null!,
            credentialManager: Mock.Of<IAccountCredentialManager>(),
            pickerStepService: CreatePickerService(),
            credentialCollector: collector,
            session: session,
            licenseChecker: null!,
            stepBuilder: CreateStandardStepBuilder());

    private static GamingAccountTemplate CreateGamingTemplate(SessionContext session, WizardCredentialCollector collector)
    {
        var credentialCounter = new Mock<IEvaluationCredentialCounter>();
        credentialCounter
            .Setup(c => c.CountCredentialsExcludingCurrent(It.IsAny<IEnumerable<CredentialEntry>>()))
            .Returns(0);
        var licenseChecker = new WizardLicenseChecker(Mock.Of<ILicenseService>(), credentialCounter.Object);

        return new GamingAccountTemplate(
            executor: null!,
            setupBuilder: CreateSetupBuilder(session),
            session: session,
            existingAccountPreparationService: new GamingExistingAccountPreparationService(
                licenseChecker,
                Mock.Of<IAccountCredentialManager>(),
                Mock.Of<IGamingLogonBlockHelper>(),
                Mock.Of<ISidResolver>()),
            pickerStepService: CreatePickerService(),
            credentialCollector: collector,
            stepBuilder: CreateGamingStepBuilder(session));
    }

    private static WizardCredentialCollector CreateCollector(SessionContext session, ICredentialDialogRunner dialogRunner)
    {
        var secureDesktopRunner = new Mock<ISecureDesktopRunner>();
        secureDesktopRunner
            .Setup(r => r.Run(It.IsAny<Action>()))
            .Callback<Action>(action => action());

        return new WizardCredentialCollector(
            secureDesktopRunner.Object,
            dialogRunner,
            new LambdaSessionProvider(() => session));
    }

    private static StandardAppWizardStepBuilder CreateStandardStepBuilder()
        => new(
            Mock.Of<IShortcutDiscoveryService>(),
            Mock.Of<IShortcutIconHelper>(),
            Mock.Of<IOpenFileDialogAdapterFactory>(),
            Mock.Of<IFolderBrowserDialogAdapterFactory>(),
            Mock.Of<IAppDiscoveryDialogService>(),
            Mock.Of<IExecutablePathResolver>());

    private static GamingWizardStepBuilder CreateGamingStepBuilder(SessionContext session)
        => new(
            CreateSetupHelperFactory(session),
            Mock.Of<IShortcutDiscoveryService>(),
            Mock.Of<IShortcutIconHelper>(),
            Mock.Of<IOpenFileDialogAdapterFactory>(),
            Mock.Of<IAppDiscoveryDialogService>());

    private static WizardAccountPickerService CreatePickerService()
        => new(
            Mock.Of<ILocalGroupQueryService>(),
            Mock.Of<ILocalUserProvider>(),
            Mock.Of<ISidResolver>(),
            new CredentialFilterHelper(Mock.Of<ISidResolver>()),
            new CredentialDisplayItemFactory(Mock.Of<ISidResolver>(), Mock.Of<IProfilePathResolver>()));

    private static WizardTemplateSetupBuilder CreateSetupBuilder(SessionContext session)
    {
        return new WizardTemplateSetupBuilder(
            CreateSetupHelperFactory(session),
            new WizardFolderGrantHelper(
                Mock.Of<IGrantMutatorService>(),
                Mock.Of<IQuickAccessPinService>()),
            session,
            Mock.Of<IWindowsAccountQueryService>(),
            Mock.Of<IExecutableKindService>());
    }

    private static WizardAccountSetupHelperFactory CreateSetupHelperFactory(SessionContext session)
    {
        var databaseProvider = new Mock<IDatabaseProvider>();
        databaseProvider
            .Setup(provider => provider.GetDatabase())
            .Returns(session.Database);
        session.Database.Settings.DefaultDesktopSettingsPath = @"C:\desktop.rfn";

        return new WizardAccountSetupHelperFactory(
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            databaseProvider.Object,
            null!);
    }

    private static SessionContext CreateSession() =>
        new SessionContext
        {
            Database = new AppDatabase(),
            CredentialStore = new CredentialStore()
        }.WithPinDerivedKeyTakingOwnership(TestSecretFactory.Create(32));

    private static void SelectExistingAccount(AccountPickerStep step, string sid)
    {
        var listBox = step.Controls.OfType<ListBox>().First();
        var displayItem = new CredentialDisplayItemFactory(Mock.Of<ISidResolver>(), Mock.Of<IProfilePathResolver>())
            .Create(new CredentialEntry { Id = Guid.NewGuid(), Sid = sid }, sidNames: null, hasStoredCredential: false);
        listBox.Items.Add(displayItem);
        listBox.SelectedIndex = 0;
    }
}
