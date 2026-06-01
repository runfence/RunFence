using Moq;
using RunFence.Account;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Licensing;
using RunFence.Wizard;
using RunFence.Wizard.Templates;
using Xunit;

namespace RunFence.Tests;

public class GamingExistingAccountPreparationServiceTests
{
    private const string Sid = "S-1-5-21-100-200-300-1001";

    [Fact]
    public void Prepare_WhenCredentialLicenseCheckFails_ReturnsFalseWithoutPersistingCredential()
    {
        using var pinKey = TestSecretFactory.Create(32);
        using var password = ProtectedString.FromChars("WizardPass1!".AsSpan());

        using var session = CreateSession(pinKey);
        var progress = new Mock<IWizardProgressReporter>();
        var credentialManager = new Mock<IAccountCredentialManager>(MockBehavior.Strict);
        var licenseService = new Mock<ILicenseService>(MockBehavior.Strict);
        licenseService.Setup(l => l.CanAddCredential(0)).Returns(false);
        licenseService
            .Setup(l => l.GetRestrictionMessage(EvaluationFeature.Credentials, 0))
            .Returns("Credential limit reached.");

        var credentialCounter = new Mock<IEvaluationCredentialCounter>(MockBehavior.Strict);
        credentialCounter
            .Setup(c => c.CountCredentialsExcludingCurrent(session.CredentialStore.Credentials))
            .Returns(0);

        var service = CreateService(
            credentialManager: credentialManager.Object,
            licenseChecker: new WizardLicenseChecker(licenseService.Object, credentialCounter.Object));

        var result = service.Prepare(session, Sid, password, progress.Object);

        Assert.False(result);
        progress.Verify(p => p.ReportError("Credential limit reached."), Times.Once);
        licenseService.Verify(l => l.CanAddApp(It.IsAny<int>()), Times.Never);
        credentialManager.Verify(
            m => m.AddNewCredential(It.IsAny<string>(), It.IsAny<ProtectedString?>(), It.IsAny<CredentialStore>(), It.IsAny<ISecureSecretSnapshotSource>()),
            Times.Never);
    }

    [Fact]
    public void Prepare_DoesNotPerformAppLicenseCheckBeforeCredentialPreparation()
    {
        using var pinKey = TestSecretFactory.Create(32);
        using var password = ProtectedString.FromChars("WizardPass1!".AsSpan());

        using var session = CreateSession(pinKey);

        var progress = new Mock<IWizardProgressReporter>(MockBehavior.Strict);
        var credentialManager = new Mock<IAccountCredentialManager>(MockBehavior.Strict);
        credentialManager
            .Setup(m => m.AddNewCredential(Sid, password, session.CredentialStore, session.PinDerivedKey))
            .Returns((true, Guid.NewGuid(), (string?)null));
        var licenseService = new Mock<ILicenseService>(MockBehavior.Strict);
        licenseService.Setup(l => l.CanAddCredential(0)).Returns(true);
        var credentialCounter = new Mock<IEvaluationCredentialCounter>(MockBehavior.Strict);
        credentialCounter
            .Setup(c => c.CountCredentialsExcludingCurrent(session.CredentialStore.Credentials))
            .Returns(0);

        var service = CreateService(
            credentialManager: credentialManager.Object,
            licenseChecker: new WizardLicenseChecker(licenseService.Object, credentialCounter.Object));

        var result = service.Prepare(
            session,
            Sid,
            collectedPassword: password,
            progress: progress.Object);

        Assert.True(result);
        credentialManager.Verify(
            m => m.AddNewCredential(Sid, password, session.CredentialStore, session.PinDerivedKey),
            Times.Once);
        licenseService.Verify(l => l.CanAddApp(It.IsAny<int>()), Times.Never);
        progress.Verify(p => p.ReportError(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void Prepare_WithCollectedPassword_PersistsCredentialAfterPassingLicenseChecks()
    {
        using var pinKey = TestSecretFactory.Create(32);
        using var password = ProtectedString.FromChars("WizardPass1!".AsSpan());

        using var session = CreateSession(pinKey);
        var progress = new Mock<IWizardProgressReporter>();
        var credentialManager = new Mock<IAccountCredentialManager>(MockBehavior.Strict);
        credentialManager
            .Setup(m => m.AddNewCredential(Sid, password, session.CredentialStore, session.PinDerivedKey))
            .Returns((true, Guid.NewGuid(), (string?)null));

        var licenseService = new Mock<ILicenseService>(MockBehavior.Strict);
        licenseService.Setup(l => l.CanAddCredential(0)).Returns(true);

        var credentialCounter = new Mock<IEvaluationCredentialCounter>(MockBehavior.Strict);
        credentialCounter
            .Setup(c => c.CountCredentialsExcludingCurrent(session.CredentialStore.Credentials))
            .Returns(0);

        var service = CreateService(
            credentialManager: credentialManager.Object,
            licenseChecker: new WizardLicenseChecker(licenseService.Object, credentialCounter.Object));

        var result = service.Prepare(session, Sid, password, progress.Object);

        Assert.True(result);
        credentialManager.Verify(
            m => m.AddNewCredential(Sid, password, session.CredentialStore, session.PinDerivedKey),
            Times.Once);
        progress.Verify(p => p.ReportError(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void Prepare_WhenCredentialPersistenceFails_ReportsCredentialErrorAndContinues()
    {
        using var pinKey = TestSecretFactory.Create(32);
        using var password = ProtectedString.FromChars("WizardPass1!".AsSpan());

        using var session = CreateSession(pinKey);
        var progress = new Mock<IWizardProgressReporter>();
        var credentialManager = new Mock<IAccountCredentialManager>(MockBehavior.Strict);
        credentialManager
            .Setup(m => m.AddNewCredential(Sid, password, session.CredentialStore, session.PinDerivedKey))
            .Returns((false, null, "duplicate"));

        var licenseService = new Mock<ILicenseService>(MockBehavior.Strict);
        licenseService.Setup(l => l.CanAddCredential(0)).Returns(true);

        var credentialCounter = new Mock<IEvaluationCredentialCounter>(MockBehavior.Strict);
        credentialCounter
            .Setup(c => c.CountCredentialsExcludingCurrent(session.CredentialStore.Credentials))
            .Returns(0);

        var service = CreateService(
            credentialManager: credentialManager.Object,
            licenseChecker: new WizardLicenseChecker(licenseService.Object, credentialCounter.Object));

        var result = service.Prepare(session, Sid, password, progress.Object);

        Assert.True(result);
        progress.Verify(p => p.ReportError("Credential: duplicate"), Times.Once);
    }

    [Fact]
    public void Prepare_UsesCachedSidNameForLogonPromptBeforeLiveResolution()
    {
        using var pinKey = TestSecretFactory.Create(32);

        using var session = CreateSession(pinKey);
        session.Database.SidNames[Sid] = @"CACHE\StoredName";

        var progress = new Mock<IWizardProgressReporter>();
        var logonHelper = new Mock<IGamingLogonBlockHelper>(MockBehavior.Strict);
        logonHelper
            .Setup(h => h.CheckAndPromptLogonUnblock(
                Sid,
                "StoredName",
                null,
                progress.Object));

        var sidResolver = new Mock<ISidResolver>(MockBehavior.Strict);
        sidResolver.Setup(r => r.TryResolveName(Sid)).Returns(@"LIVE\DifferentName");

        var service = CreateService(
            credentialManager: new Mock<IAccountCredentialManager>(MockBehavior.Strict).Object,
            licenseChecker: new WizardLicenseChecker(
                new Mock<ILicenseService>(MockBehavior.Strict).Object,
                new Mock<IEvaluationCredentialCounter>(MockBehavior.Strict).Object),
            logonBlockHelper: logonHelper.Object,
            sidResolver: sidResolver.Object);

        var result = service.Prepare(session, Sid, collectedPassword: null, progress.Object);

        Assert.True(result);
        logonHelper.VerifyAll();
    }

    private static GamingExistingAccountPreparationService CreateService(
        IAccountCredentialManager credentialManager,
        WizardLicenseChecker licenseChecker,
        IGamingLogonBlockHelper? logonBlockHelper = null,
        ISidResolver? sidResolver = null)
    {
        if (logonBlockHelper == null)
        {
            var accountRestriction = new Mock<IAccountLoginRestrictionService>(MockBehavior.Strict);
            accountRestriction.Setup(r => r.IsLoginBlockedBySid(Sid)).Returns(false);
            logonBlockHelper = new GamingLogonBlockHelper(
                accountRestriction.Object,
                new Mock<IAccountToggleService>(MockBehavior.Strict).Object);
        }

        if (sidResolver == null)
        {
            var sidResolverMock = new Mock<ISidResolver>(MockBehavior.Strict);
            sidResolverMock.Setup(r => r.TryResolveName(Sid)).Returns(@"MACHINE\Gamer");
            sidResolver = sidResolverMock.Object;
        }

        return new GamingExistingAccountPreparationService(
            licenseChecker,
            credentialManager,
            logonBlockHelper,
            sidResolver);
    }

    private static SessionContext CreateSession(SecureSecret pinKey)
        => new SessionContext
        {
            Database = new AppDatabase(),
            CredentialStore = new CredentialStore()
        }.WithClonedPinDerivedKey(pinKey);
}
