using Moq;
using RunFence.Account;
using RunFence.Account.UI;
using RunFence.Apps.Shortcuts;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Licensing;
using RunFence.Launching.Resolution;
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
        StaTestHelper.RunOnSta(() =>
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
                pickerStep.OnCommitBeforeNextAsync(Mock.Of<IWizardProgressReporter>()).GetAwaiter().GetResult();

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
        StaTestHelper.RunOnSta(() =>
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
                pickerStep.OnCommitBeforeNextAsync(Mock.Of<IWizardProgressReporter>()).GetAwaiter().GetResult();

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
            pickerStepFactory: CreatePickerFactory(),
            credentialCollector: collector,
            session: session,
            licenseChecker: null!,
            discoveryService: Mock.Of<IShortcutDiscoveryService>(),
            iconHelper: Mock.Of<IShortcutIconHelper>(),
            executablePathResolver: Mock.Of<IExecutablePathResolver>());

    private static GamingAccountTemplate CreateGamingTemplate(SessionContext session, WizardCredentialCollector collector)
    {
        var credentialCounter = new Mock<IEvaluationCredentialCounter>();
        credentialCounter
            .Setup(c => c.CountCredentialsExcludingCurrent(It.IsAny<IEnumerable<CredentialEntry>>()))
            .Returns(0);
        var licenseChecker = new WizardLicenseChecker(Mock.Of<ILicenseService>(), credentialCounter.Object);

        return new GamingAccountTemplate(
            executor: null!,
            setupHelperFactory: null!,
            folderGrantHelper: null!,
            session: session,
            existingAccountPreparationService: new GamingExistingAccountPreparationService(
                licenseChecker,
                Mock.Of<IAccountCredentialManager>(),
                Mock.Of<IGamingLogonBlockHelper>(),
                Mock.Of<ISidResolver>()),
            windowsAccountQueryService: Mock.Of<IWindowsAccountQueryService>(),
            pickerStepFactory: CreatePickerFactory(),
            credentialCollector: collector,
            discoveryService: Mock.Of<IShortcutDiscoveryService>(),
            iconHelper: Mock.Of<IShortcutIconHelper>());
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

    private static WizardAccountPickerStepFactory CreatePickerFactory()
        => new(
            Mock.Of<ILocalGroupMembershipService>(),
            Mock.Of<ILocalUserProvider>(),
            Mock.Of<ISidResolver>(),
            new CredentialFilterHelper(Mock.Of<ISidResolver>()),
            new CredentialDisplayItemFactory(Mock.Of<ISidResolver>(), Mock.Of<IProfilePathResolver>()));

    private static SessionContext CreateSession() =>
        new SessionContext
        {
            Database = new AppDatabase(),
            CredentialStore = new CredentialStore()
        }.WithOwnedPinDerivedKey(TestSecretFactory.Create(32));

    private static void SelectExistingAccount(AccountPickerStep step, string sid)
    {
        var listBox = step.Controls.OfType<ListBox>().First();
        var displayItem = new CredentialDisplayItemFactory(Mock.Of<ISidResolver>(), Mock.Of<IProfilePathResolver>())
            .Create(new CredentialEntry { Id = Guid.NewGuid(), Sid = sid }, sidNames: null, hasStoredCredential: false);
        listBox.Items.Add(displayItem);
        listBox.SelectedIndex = 0;
    }
}
