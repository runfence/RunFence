using Moq;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Persistence;
using RunFence.Security;
using RunFence.Startup.UI;
using Xunit;

namespace RunFence.Tests;

public sealed class PinChangeOrchestratorTests : IDisposable
{
    private readonly Mock<IAppConfigService> _appConfigService = new();
    private readonly Mock<IRememberPinService> _rememberPinService = new();
    private readonly Mock<ILoggingService> _log = new();
    private readonly SessionContext _session;

    public PinChangeOrchestratorTests()
    {
        using var initialKey = TestSecretFactory.FromBytes(Enumerable.Repeat((byte)0x11, 32).ToArray());
        _session = new SessionContext
        {
            Database = new AppDatabase(),
            CredentialStore = new CredentialStore { ArgonSalt = new byte[32], EncryptedCanary = [1, 2, 3] }
        }.WithOwnedPinDerivedKey(initialKey);
    }

    public void Dispose() => _session.Dispose();

    [Fact]
    public void ApplyKeyRotation_ReencryptsAndReplacesSessionStateBeforeCallback()
    {
        var rotatedStore = new CredentialStore { ArgonSalt = new byte[32], EncryptedCanary = [7, 8, 9] };
        var oldKey = _session.PinDerivedKey;
        var expectedNewKeyBytes = Enumerable.Repeat((byte)0x42, 32).ToArray();
        using var rotationResult = new PinKeyRotationResult(rotatedStore, TestSecretFactory.FromBytes(expectedNewKeyBytes));
        var sut = CreateSut();
        ISecureSecretSnapshotSource? keyPassedToSave = null;
        ISecureSecretSnapshotSource? sessionKeyDuringSave = null;

        _appConfigService
            .Setup(service => service.ReencryptAndSaveAll(
                rotatedStore,
                _session.Database,
                It.IsAny<ISecureSecretSnapshotSource>()))
            .Callback<CredentialStore, AppDatabase, ISecureSecretSnapshotSource>((_, _, key) =>
            {
                keyPassedToSave = key;
                sessionKeyDuringSave = _session.PinDerivedKey;
            });

        ISecureSecretSnapshotSource? callbackKey = null;
        CredentialStore? callbackStore = null;
        var callbackCount = 0;

        sut.ApplyKeyRotation(_session, rotationResult, () =>
        {
            callbackCount++;
            callbackKey = _session.PinDerivedKey;
            callbackStore = _session.CredentialStore;
        });

        Assert.Equal(1, callbackCount);
        Assert.Same(rotatedStore, _session.CredentialStore);
        Assert.Same(rotatedStore, callbackStore);
        Assert.Same(_session.PinDerivedKey, callbackKey);
        Assert.NotNull(keyPassedToSave);
        Assert.NotNull(sessionKeyDuringSave);
        Assert.NotSame(oldKey, keyPassedToSave);
        Assert.Same(oldKey, sessionKeyDuringSave);
        Assert.NotSame(oldKey, _session.PinDerivedKey);
        Assert.Equal(expectedNewKeyBytes, _session.PinDerivedKey.TransformSnapshot(data => data.ToArray()));
        Assert.Equal(expectedNewKeyBytes, keyPassedToSave!.TransformSnapshot(data => data.ToArray()));
        Assert.Throws<ObjectDisposedException>(() => oldKey.UseSnapshot(_ => { }));
        _appConfigService.Verify(service => service.ReencryptAndSaveAll(
            rotatedStore,
            _session.Database,
            It.IsAny<ISecureSecretSnapshotSource>()), Times.Once);
    }

    [Fact]
    public void ApplyKeyRotation_UpdateRememberPinTrue_UsesSessionKeySource()
    {
        var rotatedStore = new CredentialStore();
        using var rotationResult = new PinKeyRotationResult(rotatedStore, TestSecretFactory.Create(32, 0x33));
        var sut = CreateSut();

        ISecureSecretSnapshotSource? rememberPinKey = null;
        _rememberPinService
            .Setup(service => service.UpdateForPinChange(It.IsAny<ISecureSecretSnapshotSource>()))
            .Callback<ISecureSecretSnapshotSource>(key => rememberPinKey = key);

        sut.ApplyKeyRotation(_session, rotationResult, () => { }, updateRememberPin: true);

        Assert.Same(_session.PinDerivedKey, rememberPinKey);
    }

    [Fact]
    public void ApplyKeyRotation_UpdateRememberPinFalse_SkipsRememberPinRefresh()
    {
        using var rotationResult = new PinKeyRotationResult(new CredentialStore(), TestSecretFactory.Create(32, 0x22));
        var sut = CreateSut();

        sut.ApplyKeyRotation(_session, rotationResult, () => { }, updateRememberPin: false);

        _rememberPinService.Verify(service => service.UpdateForPinChange(It.IsAny<ISecureSecretSnapshotSource>()), Times.Never);
        _rememberPinService.Verify(service => service.Disable(), Times.Never);
    }

    [Fact]
    public void ApplyKeyRotation_RememberPinRefreshFails_DisablesAndLogsWarning()
    {
        using var rotationResult = new PinKeyRotationResult(new CredentialStore(), TestSecretFactory.Create(32, 0x55));
        var sut = CreateSut();
        _rememberPinService
            .Setup(service => service.UpdateForPinChange(It.IsAny<ISecureSecretSnapshotSource>()))
            .Throws(new InvalidOperationException("reseal failure"));

        sut.ApplyKeyRotation(_session, rotationResult, () => { }, updateRememberPin: true);

        _rememberPinService.Verify(service => service.Disable(), Times.Once);
        _log.Verify(service => service.Warn(It.Is<string>(message => message.Contains("Failed to refresh Remember PIN key after PIN rotation"))), Times.Once);
    }

    private PinChangeOrchestrator CreateSut()
        => new(
            Mock.Of<IPinService>(),
            _appConfigService.Object,
            _rememberPinService.Object,
            _log.Object,
            Mock.Of<IModalCoordinator>());
}
