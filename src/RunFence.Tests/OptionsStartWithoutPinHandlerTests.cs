using Moq;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Licensing;
using RunFence.Persistence;
using RunFence.Security;
using RunFence.Startup.UI;
using RunFence.UI;
using Xunit;

namespace RunFence.Tests;

public class OptionsStartWithoutPinHandlerTests : IDisposable
{
    private readonly Mock<IRememberPinService> _rememberPinService = new();
    private readonly Mock<IAppConfigService> _appConfigService = new();
    private readonly Mock<IRememberPinService> _rememberPinServiceForOrchestrator = new();
    private readonly Mock<ISessionProvider> _sessionProvider = new();
    private readonly Mock<IStartWithoutPinPromptService> _promptService = new();
    private readonly Mock<IStartWithoutPinRotationRunner> _rotationRunner = new();
    private readonly Mock<ILicenseService> _licenseService = new();
    private readonly Mock<ILoggingService> _log = new();

    private readonly SecureSecret _pinKey;
    private readonly SessionContext _session;
    private readonly CredentialStore _rotatedStore;
    private readonly PinChangeOrchestrator _pinChangeOrchestrator;
    private readonly OptionsStartWithoutPinHandler _handler;

    public OptionsStartWithoutPinHandlerTests()
    {
        _pinKey = TestSecretFactory.Create(32);
        _session = new SessionContext
{
            Database = new AppDatabase(),
            CredentialStore = new CredentialStore { ArgonSalt = new byte[32], EncryptedCanary = [1, 2, 3] },
        }.WithClonedPinDerivedKey(_pinKey);

        _rotatedStore = new CredentialStore { ArgonSalt = new byte[32], EncryptedCanary = [4, 5, 6] };

        _sessionProvider.Setup(s => s.GetSession()).Returns(_session);

        _appConfigService.Setup(a => a.ReencryptAndSaveAll(
            It.IsAny<CredentialStore>(),
            It.IsAny<AppDatabase>(),
            It.IsAny<ISecureSecretSnapshotSource>()));

        _pinChangeOrchestrator = new PinChangeOrchestrator(
            Mock.Of<IPinService>(),
            _appConfigService.Object,
            _rememberPinServiceForOrchestrator.Object,
            _log.Object,
            Mock.Of<IModalCoordinator>());

        _handler = new OptionsStartWithoutPinHandler(
            _rememberPinService.Object,
            _pinChangeOrchestrator,
            _sessionProvider.Object,
            _promptService.Object,
            _rotationRunner.Object,
            _licenseService.Object,
            _log.Object);
    }

    public void Dispose()
    {
        _pinKey.Dispose();
        _session.Dispose();
    }

    private void SetupRotationSuccess()
    {
        _rotationRunner.Setup(r => r.Run(It.IsAny<string>(), _session))
            .Returns(new PinKeyRotationResult(_rotatedStore, new SecureSecret(32, data => data.Fill(7))));
    }

    private void SetupRotationCancelled()
    {
        _rotationRunner.Setup(r => r.Run(It.IsAny<string>(), _session))
            .Returns((PinKeyRotationResult?)null);
    }

    // ─────────────────────────────────────────────────────────────────
    // Enable path
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Enable_WithTpmAvailable_ConfirmsWarning_RunsRotation_EnablesWithTpm()
    {
        _promptService.Setup(p => p.ConfirmSecurityWarning()).Returns(true);
        _rememberPinService.Setup(r => r.IsTpmAvailable()).Returns(true);
        SetupRotationSuccess();

        var callbackCalled = false;
        _handler.SetStartWithoutPin(true, () => callbackCalled = true);

        _promptService.Verify(p => p.ConfirmSecurityWarning(), Times.Once);
        _rotationRunner.Verify(r => r.Run("Confirm PIN to enable Start Without PIN:", _session), Times.Once);
        _rememberPinService.Verify(r => r.EnableWithTpm(It.IsAny<ISecureSecretSnapshotSource>()), Times.Once);
        _rememberPinService.Verify(r => r.EnableDpapiOnly(It.IsAny<ISecureSecretSnapshotSource>()), Times.Never);
        Assert.True(callbackCalled);
        Assert.Same(_rotatedStore, _session.CredentialStore);
    }

    [Fact]
    public void Enable_WithTpmNotAvailable_ConfirmsDpapiWarning_ThenRunsRotation_EnablesDpapiOnly()
    {
        _promptService.Setup(p => p.ConfirmSecurityWarning()).Returns(true);
        _promptService.Setup(p => p.ConfirmDpapiOnlyWarning()).Returns(true);
        _rememberPinService.Setup(r => r.IsTpmAvailable()).Returns(false);
        SetupRotationSuccess();

        _handler.SetStartWithoutPin(true, () => { });

        _promptService.Verify(p => p.ConfirmDpapiOnlyWarning(), Times.Once);
        _rememberPinService.Verify(r => r.EnableDpapiOnly(It.IsAny<ISecureSecretSnapshotSource>()), Times.Once);
        _rememberPinService.Verify(r => r.EnableWithTpm(It.IsAny<ISecureSecretSnapshotSource>()), Times.Never);
    }

    [Fact]
    public void Enable_CallsOnKeyRotatedAfterSessionKeyReplacement()
    {
        _promptService.Setup(p => p.ConfirmSecurityWarning()).Returns(true);
        _rememberPinService.Setup(r => r.IsTpmAvailable()).Returns(true);
        SetupRotationSuccess();

        var callbackCalled = false;
        var keyBeforeCallback = _session.PinDerivedKey;
        ISecureSecretSnapshotSource? keySeenByCallback = null;
        _handler.SetStartWithoutPin(true, () =>
        {
            callbackCalled = true;
            keySeenByCallback = _session.PinDerivedKey;
        });

        Assert.True(callbackCalled);
        Assert.NotSame(keyBeforeCallback, keySeenByCallback);
        Assert.Same(_session.PinDerivedKey, keySeenByCallback);
        Assert.Same(_rotatedStore, _session.CredentialStore);
    }

    [Fact]
    public void Enable_CancelAtSecurityWarning_DoesNotRunRotationOrEnableRememberPin()
    {
        _promptService.Setup(p => p.ConfirmSecurityWarning()).Returns(false);

        _handler.SetStartWithoutPin(true, () => { });

        _rotationRunner.Verify(r => r.Run(It.IsAny<string>(), It.IsAny<SessionContext>()), Times.Never);
        _rememberPinService.Verify(r => r.EnableWithTpm(It.IsAny<ISecureSecretSnapshotSource>()), Times.Never);
        _rememberPinService.Verify(r => r.EnableDpapiOnly(It.IsAny<ISecureSecretSnapshotSource>()), Times.Never);
    }

    [Fact]
    public void Enable_CancelAtDpapiOnlyWarning_DoesNotRunRotationOrEnableRememberPin()
    {
        _promptService.Setup(p => p.ConfirmSecurityWarning()).Returns(true);
        _rememberPinService.Setup(r => r.IsTpmAvailable()).Returns(false);
        _promptService.Setup(p => p.ConfirmDpapiOnlyWarning()).Returns(false);

        _handler.SetStartWithoutPin(true, () => { });

        _rotationRunner.Verify(r => r.Run(It.IsAny<string>(), It.IsAny<SessionContext>()), Times.Never);
        _rememberPinService.Verify(r => r.EnableWithTpm(It.IsAny<ISecureSecretSnapshotSource>()), Times.Never);
        _rememberPinService.Verify(r => r.EnableDpapiOnly(It.IsAny<ISecureSecretSnapshotSource>()), Times.Never);
    }

    [Fact]
    public void Enable_CancelAtPinDialog_DoesNotApplyKeyRotationOrEnableRememberPin()
    {
        _promptService.Setup(p => p.ConfirmSecurityWarning()).Returns(true);
        _rememberPinService.Setup(r => r.IsTpmAvailable()).Returns(true);
        SetupRotationCancelled();

        bool onKeyRotatedCalled = false;
        _handler.SetStartWithoutPin(true, () => { onKeyRotatedCalled = true; });

        Assert.False(onKeyRotatedCalled);
        _rememberPinService.Verify(r => r.EnableWithTpm(It.IsAny<ISecureSecretSnapshotSource>()), Times.Never);
        _rememberPinService.Verify(r => r.EnableDpapiOnly(It.IsAny<ISecureSecretSnapshotSource>()), Times.Never);
        _appConfigService.Verify(a => a.ReencryptAndSaveAll(
            It.IsAny<CredentialStore>(), It.IsAny<AppDatabase>(), It.IsAny<ISecureSecretSnapshotSource>()), Times.Never);
    }

    [Fact]
    public void Enable_TpmEncryptionFails_FallsBackToDpapi_ShowsTpmFallbackWarning()
    {
        _promptService.Setup(p => p.ConfirmSecurityWarning()).Returns(true);
        _rememberPinService.Setup(r => r.IsTpmAvailable()).Returns(true);
        SetupRotationSuccess();
        _rememberPinService.Setup(r => r.EnableWithTpm(It.IsAny<ISecureSecretSnapshotSource>()))
            .Throws(new Exception("TPM error"));

        _handler.SetStartWithoutPin(true, () => { });

        _rememberPinService.Verify(r => r.EnableDpapiOnly(It.IsAny<ISecureSecretSnapshotSource>()), Times.Once);
        _promptService.Verify(p => p.ShowTpmFallbackWarning(), Times.Once);
        _promptService.Verify(p => p.ShowError(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void Enable_EnableDpapiFailsAfterKeyRotation_DisablesRememberPinAndShowsError()
    {
        _promptService.Setup(p => p.ConfirmSecurityWarning()).Returns(true);
        _rememberPinService.Setup(r => r.IsTpmAvailable()).Returns(false);
        _promptService.Setup(p => p.ConfirmDpapiOnlyWarning()).Returns(true);
        SetupRotationSuccess();
        _rememberPinService.Setup(r => r.EnableDpapiOnly(It.IsAny<ISecureSecretSnapshotSource>()))
            .Throws(new Exception("DPAPI error"));

        _handler.SetStartWithoutPin(true, () => { });

        _rememberPinService.Verify(r => r.Disable(), Times.Once);
        _log.Verify(l => l.Error(It.IsAny<string>(), It.IsAny<Exception>()), Times.Once);
        _promptService.Verify(p => p.ShowError(It.IsAny<string>(), It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public void Enable_EnableDpapiFailsAfterKeyRotation_DisableAlsoThrows_LogsWarningAndContinues()
    {
        _promptService.Setup(p => p.ConfirmSecurityWarning()).Returns(true);
        _rememberPinService.Setup(r => r.IsTpmAvailable()).Returns(false);
        _promptService.Setup(p => p.ConfirmDpapiOnlyWarning()).Returns(true);
        SetupRotationSuccess();
        _rememberPinService.Setup(r => r.EnableDpapiOnly(It.IsAny<ISecureSecretSnapshotSource>()))
            .Throws(new Exception("DPAPI error"));
        _rememberPinService.Setup(r => r.Disable())
            .Throws(new Exception("Disable failed"));

        var exception = Record.Exception(() => _handler.SetStartWithoutPin(true, () => { }));

        Assert.Null(exception);
        _log.Verify(l => l.Warn(It.IsAny<string>()), Times.AtLeastOnce);
    }

    // ─────────────────────────────────────────────────────────────────
    // Disable path
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Disable_RunsRotation_DisablesRememberPin_AppliesKeyRotation()
    {
        SetupRotationSuccess();

        var callbackCalled = false;
        _handler.SetStartWithoutPin(false, () => callbackCalled = true);

        _rotationRunner.Verify(r => r.Run("Confirm PIN to disable Start Without PIN:", _session), Times.Once);
        _rememberPinService.Verify(r => r.Disable(), Times.Once);
        Assert.True(callbackCalled);
        Assert.Same(_rotatedStore, _session.CredentialStore);
    }

    [Fact]
    public void Disable_CancelAtPinDialog_DoesNotDisableRememberPinOrApplyKeyRotation()
    {
        SetupRotationCancelled();

        bool onKeyRotatedCalled = false;
        _handler.SetStartWithoutPin(false, () => { onKeyRotatedCalled = true; });

        _rememberPinService.Verify(r => r.Disable(), Times.Never);
        Assert.False(onKeyRotatedCalled);
        _appConfigService.Verify(a => a.ReencryptAndSaveAll(
            It.IsAny<CredentialStore>(), It.IsAny<AppDatabase>(), It.IsAny<ISecureSecretSnapshotSource>()), Times.Never);
    }

    [Fact]
    public void Disable_DisableThrows_LogsErrorAndShowsErrorMessage_DoesNotApplyKeyRotation()
    {
        SetupRotationSuccess();
        _rememberPinService.Setup(r => r.Disable()).Throws(new Exception("Disable error"));

        bool onKeyRotatedCalled = false;
        _handler.SetStartWithoutPin(false, () => { onKeyRotatedCalled = true; });

        Assert.False(onKeyRotatedCalled);
        _log.Verify(l => l.Error(It.IsAny<string>(), It.IsAny<Exception>()), Times.Once);
        _promptService.Verify(p => p.ShowError(It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        _appConfigService.Verify(a => a.ReencryptAndSaveAll(
            It.IsAny<CredentialStore>(), It.IsAny<AppDatabase>(), It.IsAny<ISecureSecretSnapshotSource>()), Times.Never);
    }

    [Fact]
    public void Disable_DoesNotCheckTpmAvailabilityOrShowSecurityWarning()
    {
        SetupRotationSuccess();

        _handler.SetStartWithoutPin(false, () => { });

        _promptService.Verify(p => p.ConfirmSecurityWarning(), Times.Never);
        _promptService.Verify(p => p.ConfirmDpapiOnlyWarning(), Times.Never);
        _rememberPinService.Verify(r => r.IsTpmAvailable(), Times.Never);
    }

    // ─────────────────────────────────────────────────────────────────
    // Key rotation correctness
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Enable_ApplyKeyRotation_ReencryptsAllConfigs()
    {
        _promptService.Setup(p => p.ConfirmSecurityWarning()).Returns(true);
        _rememberPinService.Setup(r => r.IsTpmAvailable()).Returns(true);
        SetupRotationSuccess();

        _handler.SetStartWithoutPin(true, () => { });

        _appConfigService.Verify(a => a.ReencryptAndSaveAll(
            _rotatedStore,
            _session.Database,
            It.IsAny<ISecureSecretSnapshotSource>()), Times.Once);
    }

    [Fact]
    public void Disable_ApplyKeyRotation_ReencryptsAllConfigs()
    {
        SetupRotationSuccess();

        _handler.SetStartWithoutPin(false, () => { });

        _appConfigService.Verify(a => a.ReencryptAndSaveAll(
            _rotatedStore,
            _session.Database,
            It.IsAny<ISecureSecretSnapshotSource>()), Times.Once);
    }

}
