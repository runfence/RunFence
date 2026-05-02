using System.Security.Cryptography;
using System.Text.Json;
using Moq;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Persistence;
using RunFence.Security;
using RunFence.Startup;
using RunFence.Startup.UI;
using Xunit;

namespace RunFence.Tests;

public class StartupCredentialLoaderTests
{
    private readonly Mock<IStartupUI> _ui = new();
    private readonly Mock<IDatabaseService> _databaseService = new();
    private readonly Mock<IConfigPaths> _configPaths = new();
    private readonly Mock<IRememberPinService> _rememberPinService = new();
    private readonly Mock<ILoggingService> _log = new();
    private readonly Mock<ICredentialEncryptionService> _encryptionService = new();

    private readonly PinService _pinService;
    private readonly CredentialStore _store;
    private readonly byte[] _pinDerivedKey;

    public StartupCredentialLoaderTests()
    {
        _pinService = new PinService(new CredentialEncryptionService(), argon2MemoryKb: 1024, argon2Iterations: 1, argon2Parallelism: 1);

        using var testPin = ProtectedString.FromChars("testpin".AsSpan());
        var (store, key) = _pinService.ResetPin(testPin);
        _store = store;
        _pinDerivedKey = key;

        _databaseService.Setup(d => d.LoadCredentialStore()).Returns(_store);
        _databaseService.Setup(d => d.TryGetConfigSalt()).Returns((byte[]?)null);
        _configPaths.Setup(p => p.CredentialsFilePath).Returns(@"C:\fake\credentials.dat");
    }

    private StartupCredentialLoader BuildLoader() =>
        new(_ui.Object, _databaseService.Object, _configPaths.Object,
            _rememberPinService.Object, _pinService, _encryptionService.Object, _log.Object);

    // Creates a store with a non-current-account credential so VerifyDpapiAccess invokes Decrypt.
    // The synthetic SID "S-1-5-21-999-999-999-1001" will never match the test process SID.
    private CredentialStore BuildStoreWithCredential() => new()
    {
        ArgonSalt = _store.ArgonSalt,
        EncryptedCanary = _store.EncryptedCanary,
        Credentials =
        [
            new CredentialEntry
            {
                Sid = "S-1-5-21-999-999-999-1001",
                EncryptedPassword = [1, 2, 3, 4]
            }
        ]
    };

    [Fact]
    public void LoadAndVerifyCredentials_RememberPinEnabledAndValid_ReturnsPinBypassed()
    {
        // Arrange
        _rememberPinService.Setup(a => a.IsEnabled).Returns(true);
        _rememberPinService.Setup(a => a.TryDecrypt(out It.Ref<byte[]>.IsAny))
            .Returns((ref byte[] key) =>
            {
                key = (byte[])_pinDerivedKey.Clone();
                return true;
            });

        var loader = BuildLoader();

        // Act
        var result = loader.LoadAndVerifyCredentials();

        // Assert
        Assert.NotNull(result);
        Assert.True(result.PinBypassed);
        Assert.Null(result.MismatchKey);
        Assert.Equal(_store, result.Store);
        Assert.Equal(_pinDerivedKey, result.PinDerivedKey);
        _ui.Verify(u => u.PromptVerifyPin(It.IsAny<CredentialStore>(), It.IsAny<byte[]?>()), Times.Never);
    }

    [Fact]
    public void LoadAndVerifyCredentials_RememberPinKeyFailsCanary_FallsBackToPin()
    {
        // Arrange: key that does NOT verify against the store's canary
        var wrongKey = new byte[32];
        new Random(77).NextBytes(wrongKey);

        _rememberPinService.Setup(a => a.IsEnabled).Returns(true);
        _rememberPinService.Setup(a => a.TryDecrypt(out It.Ref<byte[]>.IsAny))
            .Returns((ref byte[] key) =>
            {
                key = wrongKey;
                return true;
            });

        _ui.Setup(u => u.PromptVerifyPin(_store, null))
            .Returns(new PinVerifyOutcome((byte[])_pinDerivedKey.Clone(), null, null));

        var loader = BuildLoader();

        // Act
        var result = loader.LoadAndVerifyCredentials();

        // Assert
        Assert.NotNull(result);
        Assert.False(result.PinBypassed);
        _ui.Verify(u => u.PromptVerifyPin(_store, null), Times.Once);
        _log.Verify(l => l.Warn(It.Is<string>(s => s.Contains("canary verification"))), Times.Once);
    }

    [Fact]
    public void LoadAndVerifyCredentials_RememberPinDecryptFails_FallsBackToPin()
    {
        // Arrange
        _rememberPinService.Setup(a => a.IsEnabled).Returns(true);
        _rememberPinService.Setup(a => a.TryDecrypt(out It.Ref<byte[]>.IsAny))
            .Returns((ref byte[] key) =>
            {
                key = Array.Empty<byte>();
                return false;
            });

        _ui.Setup(u => u.PromptVerifyPin(_store, null))
            .Returns(new PinVerifyOutcome((byte[])_pinDerivedKey.Clone(), null, null));

        var loader = BuildLoader();

        // Act
        var result = loader.LoadAndVerifyCredentials();

        // Assert
        Assert.NotNull(result);
        Assert.False(result.PinBypassed);
        _ui.Verify(u => u.PromptVerifyPin(_store, null), Times.Once);
        _log.Verify(l => l.Info(It.Is<string>(s => s.Contains("unavailable"))), Times.Once);
    }

    [Fact]
    public void LoadAndVerifyCredentials_RememberPinDisabled_GoesDirectlyToPin()
    {
        // Arrange: remembered PIN service not enabled
        _rememberPinService.Setup(a => a.IsEnabled).Returns(false);

        _ui.Setup(u => u.PromptVerifyPin(_store, null))
            .Returns(new PinVerifyOutcome((byte[])_pinDerivedKey.Clone(), null, null));

        var loader = BuildLoader();

        // Act
        var result = loader.LoadAndVerifyCredentials();

        // Assert
        Assert.NotNull(result);
        Assert.False(result.PinBypassed);
        _rememberPinService.Verify(a => a.TryDecrypt(out It.Ref<byte[]>.IsAny), Times.Never);
        _ui.Verify(u => u.PromptVerifyPin(_store, null), Times.Once);
    }

    [Fact]
    public void FileNotFound_CallsPromptNewPin()
    {
        // Arrange
        _databaseService.Setup(d => d.LoadCredentialStore()).Throws<FileNotFoundException>();

        var newStore = new CredentialStore();
        var newKey = new byte[32];
        new Random(1).NextBytes(newKey);
        _ui.Setup(u => u.PromptNewPin()).Returns((newStore, newKey));

        var loader = BuildLoader();

        // Act
        var result = loader.LoadAndVerifyCredentials();

        // Assert
        Assert.NotNull(result);
        Assert.Equal(newStore, result!.Store);
        Assert.Equal(newKey, result.PinDerivedKey);
        Assert.Null(result.MismatchKey);
        _ui.Verify(u => u.PromptNewPin(), Times.Once);
        _ui.Verify(u => u.PromptVerifyPin(It.IsAny<CredentialStore>(), It.IsAny<byte[]?>()), Times.Never);
    }

    [Fact]
    public void FileNotFound_PromptNewPinCancelled_ReturnsNull()
    {
        // Arrange: first run, but user cancels the new-PIN dialog
        _databaseService.Setup(d => d.LoadCredentialStore()).Throws<FileNotFoundException>();
        _ui.Setup(u => u.PromptNewPin()).Returns(((CredentialStore, byte[])?)null);

        var loader = BuildLoader();

        // Act
        var result = loader.LoadAndVerifyCredentials();

        // Assert
        Assert.Null(result);
        _ui.Verify(u => u.PromptNewPin(), Times.Once);
    }

    [Fact]
    public void JsonException_ShowsErrorReturnsNull()
    {
        // Arrange
        _databaseService.Setup(d => d.LoadCredentialStore()).Throws(new JsonException("corrupt"));

        var loader = BuildLoader();

        // Act
        var result = loader.LoadAndVerifyCredentials();

        // Assert
        Assert.Null(result);
        _ui.Verify(u => u.ShowError(It.Is<string>(s => s.Contains("corrupt")), It.IsAny<string>()), Times.Once);
        _log.Verify(l => l.Error(It.IsAny<string>(), It.IsAny<Exception>()), Times.Once);
    }

    [Fact]
    public void IOException_ShowsErrorReturnsNull()
    {
        // Arrange
        _databaseService.Setup(d => d.LoadCredentialStore()).Throws(new IOException("access denied"));

        var loader = BuildLoader();

        // Act
        var result = loader.LoadAndVerifyCredentials();

        // Assert
        Assert.Null(result);
        _ui.Verify(u => u.ShowError(It.Is<string>(s => s.Contains("access denied")), It.IsAny<string>()), Times.Once);
        _log.Verify(l => l.Error(It.IsAny<string>(), It.IsAny<Exception>()), Times.Once);
    }

    [Fact]
    public void DpapiLoss_PromptsRecoveryPin()
    {
        // Arrange
        var storeWithCred = BuildStoreWithCredential();
        _databaseService.Setup(d => d.LoadCredentialStore()).Returns(storeWithCred);
        _rememberPinService.Setup(a => a.IsEnabled).Returns(false);

        // PIN verification succeeds — returns valid key
        _ui.Setup(u => u.PromptVerifyPin(storeWithCred, null))
            .Returns(new PinVerifyOutcome((byte[])_pinDerivedKey.Clone(), null, null));

        // DPAPI decryption fails (DPAPI key loss)
        _encryptionService.Setup(e => e.Decrypt(It.IsAny<byte[]>(), It.IsAny<byte[]>()))
            .Throws(new CryptographicException("DPAPI loss"));

        var recoveryStore = new CredentialStore();
        var recoveryKey = new byte[32];
        new Random(2).NextBytes(recoveryKey);
        _ui.Setup(u => u.PromptRecoveryPin(null))
            .Returns(new RecoveryPinOutcome(recoveryStore, recoveryKey, null));

        var loader = BuildLoader();

        // Act
        var result = loader.LoadAndVerifyCredentials();

        // Assert
        Assert.NotNull(result);
        Assert.Equal(recoveryStore, result!.Store);
        Assert.Equal(recoveryKey, result.PinDerivedKey);
        _ui.Verify(u => u.PromptRecoveryPin(null), Times.Once);
        _log.Verify(l => l.Error(It.Is<string>(s => s.Contains("DPAPI")), It.IsAny<Exception>()), Times.Once);
    }

    [Fact]
    public void DpapiLoss_RecoveryPinCancelled_ReturnsNull()
    {
        // Arrange: same DPAPI-loss conditions, but user cancels the recovery dialog
        var storeWithCred = BuildStoreWithCredential();
        _databaseService.Setup(d => d.LoadCredentialStore()).Returns(storeWithCred);
        _rememberPinService.Setup(a => a.IsEnabled).Returns(false);

        _ui.Setup(u => u.PromptVerifyPin(storeWithCred, null))
            .Returns(new PinVerifyOutcome((byte[])_pinDerivedKey.Clone(), null, null));

        _encryptionService.Setup(e => e.Decrypt(It.IsAny<byte[]>(), It.IsAny<byte[]>()))
            .Throws(new CryptographicException("DPAPI loss"));

        _ui.Setup(u => u.PromptRecoveryPin(null)).Returns((RecoveryPinOutcome?)null);

        var loader = BuildLoader();

        // Act
        var result = loader.LoadAndVerifyCredentials();

        // Assert
        Assert.Null(result);
        _ui.Verify(u => u.PromptRecoveryPin(null), Times.Once);
    }

    [Fact]
    public void PinCancelled_ReturnsNull()
    {
        // Arrange: PIN dialog returns Key.Length == 0 (user cancelled)
        _rememberPinService.Setup(a => a.IsEnabled).Returns(false);
        _ui.Setup(u => u.PromptVerifyPin(_store, null))
            .Returns(new PinVerifyOutcome([], null, null));

        var loader = BuildLoader();

        // Act
        var result = loader.LoadAndVerifyCredentials();

        // Assert
        Assert.Null(result);
        _ui.Verify(u => u.PromptVerifyPin(_store, null), Times.Once);
    }

    [Fact]
    public void PinReset_ReturnsNewStore()
    {
        // Arrange: PIN reset from verification dialog — NewStore is set
        _rememberPinService.Setup(a => a.IsEnabled).Returns(false);

        var resetStore = new CredentialStore();
        var resetKey = new byte[32];
        new Random(3).NextBytes(resetKey);

        _ui.Setup(u => u.PromptVerifyPin(_store, null))
            .Returns(new PinVerifyOutcome(resetKey, resetStore, null));

        var loader = BuildLoader();

        // Act
        var result = loader.LoadAndVerifyCredentials();

        // Assert
        Assert.NotNull(result);
        Assert.Equal(resetStore, result!.Store);
        Assert.Equal(resetKey, result.PinDerivedKey);
        _log.Verify(l => l.Info(It.Is<string>(s => s.Contains("PIN reset"))), Times.Once);
    }

    [Fact]
    public void SaltMismatch_PassesMismatchSalt()
    {
        // Arrange: TryGetConfigSalt returns a salt different from the store's ArgonSalt
        var differentSalt = new byte[32];
        new Random(4).NextBytes(differentSalt);
        _databaseService.Setup(d => d.TryGetConfigSalt()).Returns(differentSalt);

        _rememberPinService.Setup(a => a.IsEnabled).Returns(false);

        byte[]? capturedConfigSalt = null;
        _ui.Setup(u => u.PromptVerifyPin(_store, It.IsAny<byte[]?>()))
            .Callback<CredentialStore, byte[]?>((_, salt) => capturedConfigSalt = salt)
            .Returns(new PinVerifyOutcome((byte[])_pinDerivedKey.Clone(), null, null));

        var loader = BuildLoader();

        // Act
        var result = loader.LoadAndVerifyCredentials();

        // Assert
        Assert.NotNull(result);
        // The mismatch salt (differentSalt) must have been passed to PromptVerifyPin
        Assert.NotNull(capturedConfigSalt);
        Assert.Equal(differentSalt, capturedConfigSalt!);
    }
}
