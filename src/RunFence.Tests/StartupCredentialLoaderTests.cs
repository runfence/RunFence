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

public class StartupCredentialLoaderTests : IDisposable
{
    private readonly Mock<IStartupUI> _ui = new();
    private readonly Mock<IDatabaseService> _databaseService = new();
    private readonly Mock<ILoadedGoodBackupStore> _loadedGoodBackupStore = new();
    private readonly Mock<IConfigPaths> _configPaths = new();
    private readonly Mock<IRememberPinService> _rememberPinService = new();
    private readonly Mock<ILoggingService> _log = new();
    private readonly FakeCredentialEncryptionSpanService _encryptionService = new();

    private readonly PinService _pinService;
    private readonly CredentialStore _store;
    private readonly byte[] _pinDerivedKey;
    private readonly TempDirectory _tempDir;
    private readonly string _credentialsFilePath;
    private readonly string _selectedConfigPath;

    public StartupCredentialLoaderTests()
    {
        _tempDir = new TempDirectory("RunFence_StartupCredentialLoaderTests");
        _credentialsFilePath = Path.Combine(_tempDir.Path, "credentials.dat");
        _selectedConfigPath = Path.Combine(_tempDir.Path, "config.dat");
        _pinService = new PinService(new CredentialEncryptionService(new NativeDpapiProtector()), argon2MemoryKb: 1024, argon2Iterations: 1, argon2Parallelism: 1);

        using var testPin = ProtectedString.FromChars("testpin".AsSpan());
        using var resetResult = _pinService.ResetPin(testPin);
        _store = resetResult.Store;
        using var key = resetResult.TakePinDerivedKey();
        _pinDerivedKey = key.TransformSnapshot(data => data.ToArray());

        _databaseService.Setup(d => d.LoadCredentialStore()).Returns(_store);
        _databaseService.Setup(d => d.TryGetConfigSaltFromPath(_selectedConfigPath)).Returns((byte[]?)null);
        _configPaths.Setup(p => p.CredentialsFilePath).Returns(_credentialsFilePath);
        _configPaths.Setup(p => p.ConfigFilePath).Returns(_selectedConfigPath);
        _loadedGoodBackupStore.Setup(b => b.GetBackupPath(_credentialsFilePath)).Returns(_credentialsFilePath + ".lastgood");
    }

    public void Dispose()
    {
        _tempDir.Dispose();
    }

    private StartupCredentialLoader BuildLoader() =>
        new(_ui.Object, _databaseService.Object, _loadedGoodBackupStore.Object, _configPaths.Object,
            _rememberPinService.Object, _pinService, _encryptionService, _log.Object);

    private static SecureSecret CreateSecret(byte[] bytes)
        => new(bytes.Length, data => bytes.AsSpan().CopyTo(data));

    private static void AssertSecretEqual(byte[] expected, SecureSecret secret)
    {
        var actual = secret.TransformSnapshot(data => data.ToArray());
        Assert.Equal(expected, actual);
    }

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
    public void LoadAndVerifyCredentials_RememberPinEnabledAndValidButConfigSaltUnknown_ReturnsPinBypassed()
    {
        _rememberPinService.Setup(a => a.IsEnabled).Returns(true);
        _rememberPinService.Setup(a => a.TryDecryptSecret(out It.Ref<SecureSecret?>.IsAny))
            .Returns((out SecureSecret? key) =>
            {
                key = CreateSecret(_pinDerivedKey);
                return true;
            });

        var loader = BuildLoader();

        using var result = loader.LoadAndVerifyCredentials(_selectedConfigPath);

        Assert.NotNull(result);
        Assert.True(result!.PinBypassed);
        Assert.Null(result.TakeMismatchKey());
        Assert.Equal(_store, result.Store);
        using var pinKey = result.TakePinDerivedKey();
        AssertSecretEqual(_pinDerivedKey, pinKey);
        _ui.Verify(u => u.PromptVerifyPin(It.IsAny<CredentialStore>(), It.IsAny<byte[]?>()), Times.Never);
    }

    [Fact]
    public void LoadAndVerifyCredentials_RememberPinEnabledAndValidAndConfigSaltMatches_ReturnsPinBypassed()
    {
        _databaseService.Setup(d => d.TryGetConfigSaltFromPath(_selectedConfigPath)).Returns((byte[])_store.ArgonSalt.Clone());
        _rememberPinService.Setup(a => a.IsEnabled).Returns(true);
        _rememberPinService.Setup(a => a.TryDecryptSecret(out It.Ref<SecureSecret?>.IsAny))
            .Returns((out SecureSecret? key) =>
            {
                key = CreateSecret(_pinDerivedKey);
                return true;
            });

        var loader = BuildLoader();

        using var result = loader.LoadAndVerifyCredentials(_selectedConfigPath);

        Assert.NotNull(result);
        Assert.True(result!.PinBypassed);
        Assert.Null(result.TakeMismatchKey());
        _ui.Verify(u => u.PromptVerifyPin(It.IsAny<CredentialStore>(), It.IsAny<byte[]?>()), Times.Never);
        _loadedGoodBackupStore.Verify(
            b => b.TryPreserveCurrentFile(It.IsAny<string>(), out It.Ref<string?>.IsAny),
            Times.Never);
    }

    [Fact]
    public void LoadAndVerifyCredentials_RememberPinKeyFailsCanary_FallsBackToPin()
    {
        var wrongKey = new byte[32];
        new Random(77).NextBytes(wrongKey);

        _rememberPinService.Setup(a => a.IsEnabled).Returns(true);
        _rememberPinService.Setup(a => a.TryDecryptSecret(out It.Ref<SecureSecret?>.IsAny))
            .Returns((out SecureSecret? key) =>
            {
                key = CreateSecret(wrongKey);
                return true;
            });

        _ui.Setup(u => u.PromptVerifyPin(_store, null))
            .Returns(PinVerifyOutcome.Verified(CreateSecret(_pinDerivedKey), null));

        var loader = BuildLoader();

        using var result = loader.LoadAndVerifyCredentials(_selectedConfigPath);

        Assert.NotNull(result);
        Assert.False(result!.PinBypassed);
        _ui.Verify(u => u.PromptVerifyPin(_store, null), Times.Once);
        _log.Verify(l => l.Warn(It.Is<string>(s => s.Contains("canary verification"))), Times.Once);
    }

    [Fact]
    public void LoadAndVerifyCredentials_RememberPinDecryptFails_FallsBackToPin()
    {
        _rememberPinService.Setup(a => a.IsEnabled).Returns(true);
        _rememberPinService.Setup(a => a.TryDecryptSecret(out It.Ref<SecureSecret?>.IsAny))
            .Returns((out SecureSecret? key) =>
            {
                key = null;
                return false;
            });

        _ui.Setup(u => u.PromptVerifyPin(_store, null))
            .Returns(PinVerifyOutcome.Verified(CreateSecret(_pinDerivedKey), null));

        var loader = BuildLoader();

        using var result = loader.LoadAndVerifyCredentials(_selectedConfigPath);

        Assert.NotNull(result);
        Assert.False(result!.PinBypassed);
        _ui.Verify(u => u.PromptVerifyPin(_store, null), Times.Once);
        _log.Verify(l => l.Info(It.Is<string>(s => s.Contains("unavailable"))), Times.Once);
    }

    [Fact]
    public void LoadAndVerifyCredentials_RememberPinDisabled_GoesDirectlyToPin()
    {
        _rememberPinService.Setup(a => a.IsEnabled).Returns(false);
        _ui.Setup(u => u.PromptVerifyPin(_store, null))
            .Returns(PinVerifyOutcome.Verified(CreateSecret(_pinDerivedKey), null));

        var loader = BuildLoader();

        using var result = loader.LoadAndVerifyCredentials(_selectedConfigPath);

        Assert.NotNull(result);
        Assert.False(result!.PinBypassed);
        _rememberPinService.Verify(a => a.TryDecryptSecret(out It.Ref<SecureSecret?>.IsAny), Times.Never);
        _ui.Verify(u => u.PromptVerifyPin(_store, null), Times.Once);
    }

    [Fact]
    public void FileNotFound_CallsPromptNewPin()
    {
        _databaseService.Setup(d => d.LoadCredentialStore()).Throws<FileNotFoundException>();

        var newStore = new CredentialStore();
        var newKey = new byte[32];
        new Random(1).NextBytes(newKey);
        _ui.Setup(u => u.PromptNewPin()).Returns(new PinResetResult(newStore, CreateSecret(newKey)));

        var loader = BuildLoader();

        using var result = loader.LoadAndVerifyCredentials(_selectedConfigPath);

        Assert.NotNull(result);
        Assert.Equal(newStore, result!.Store);
        using var pinKey = result.TakePinDerivedKey();
        AssertSecretEqual(newKey, pinKey);
        Assert.Null(result.TakeMismatchKey());
        _ui.Verify(u => u.PromptNewPin(), Times.Once);
        _ui.Verify(u => u.PromptVerifyPin(It.IsAny<CredentialStore>(), It.IsAny<byte[]?>()), Times.Never);
    }

    [Fact]
    public void FileNotFound_BackupExistsAndRestoreConfirmed_LoadsBackupDirectlyAndContinuesPinVerification()
    {
        var backupPath = _credentialsFilePath + ".lastgood";
        _databaseService.Setup(d => d.LoadCredentialStore())
            .Throws<FileNotFoundException>();
        _databaseService
            .Setup(d => d.LoadCredentialStoreFromPath(backupPath))
            .Returns(_store);
        _rememberPinService.Setup(a => a.IsEnabled).Returns(false);
        _loadedGoodBackupStore.Setup(b => b.Exists(_credentialsFilePath)).Returns(true);
        _ui.Setup(u => u.ConfirmRestoreCredentialStoreBackup()).Returns(true);
        _ui.Setup(u => u.PromptVerifyPin(_store, null))
            .Returns(PinVerifyOutcome.Verified(CreateSecret(_pinDerivedKey), null));

        var loader = BuildLoader();

        using var result = loader.LoadAndVerifyCredentials(_selectedConfigPath);

        Assert.NotNull(result);
        Assert.Equal(_store, result!.Store);
        _loadedGoodBackupStore.Verify(b => b.Exists(_credentialsFilePath), Times.Once);
        _ui.Verify(u => u.ConfirmRestoreCredentialStoreBackup(), Times.Once);
        _databaseService.Verify(d => d.LoadCredentialStoreFromPath(backupPath), Times.Once);
        _databaseService.Verify(d => d.SaveCredentialStore(_store), Times.Once);
        _loadedGoodBackupStore.Verify(
            b => b.TryPreserveCurrentFile(It.IsAny<string>(), out It.Ref<string?>.IsAny),
            Times.Never);
        _ui.Verify(u => u.PromptVerifyPin(_store, null), Times.Once);
        _ui.Verify(u => u.PromptNewPin(), Times.Never);
        _ui.Verify(u => u.ShowError(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void FileNotFound_BackupExistsButRestoreDeclined_FallsThroughToFirstRunPrompt()
    {
        _databaseService.Setup(d => d.LoadCredentialStore()).Throws<FileNotFoundException>();
        _loadedGoodBackupStore.Setup(b => b.Exists(_credentialsFilePath)).Returns(true);
        _ui.Setup(u => u.ConfirmRestoreCredentialStoreBackup()).Returns(false);

        var newStore = new CredentialStore();
        var newKey = new byte[32];
        new Random(11).NextBytes(newKey);
        _ui.Setup(u => u.PromptNewPin()).Returns(new PinResetResult(newStore, CreateSecret(newKey)));

        var loader = BuildLoader();

        using var result = loader.LoadAndVerifyCredentials(_selectedConfigPath);

        Assert.NotNull(result);
        Assert.Equal(newStore, result!.Store);
        _loadedGoodBackupStore.Verify(b => b.Exists(_credentialsFilePath), Times.Once);
        _ui.Verify(u => u.ConfirmRestoreCredentialStoreBackup(), Times.Once);
        _databaseService.Verify(d => d.LoadCredentialStoreFromPath(It.IsAny<string>()), Times.Never);
        _ui.Verify(u => u.PromptVerifyPin(It.IsAny<CredentialStore>(), It.IsAny<byte[]?>()), Times.Never);
        _ui.Verify(u => u.PromptNewPin(), Times.Once);
    }

    [Fact]
    public void FileNotFound_BackupStoreStillMissing_BackupAttemptRemainsSingleShot()
    {
        var backupPath = _credentialsFilePath + ".lastgood";
        _databaseService.Setup(d => d.LoadCredentialStore())
            .Throws<FileNotFoundException>();
        _databaseService
            .Setup(d => d.LoadCredentialStoreFromPath(backupPath))
            .Throws<FileNotFoundException>();
        _loadedGoodBackupStore.Setup(b => b.Exists(_credentialsFilePath)).Returns(true);
        _ui.Setup(u => u.ConfirmRestoreCredentialStoreBackup()).Returns(true);

        var newStore = new CredentialStore();
        var newKey = new byte[32];
        new Random(12).NextBytes(newKey);
        _ui.Setup(u => u.PromptNewPin()).Returns(new PinResetResult(newStore, CreateSecret(newKey)));

        var loader = BuildLoader();

        using var result = loader.LoadAndVerifyCredentials(_selectedConfigPath);

        Assert.NotNull(result);
        Assert.Equal(newStore, result!.Store);
        _loadedGoodBackupStore.Verify(b => b.Exists(_credentialsFilePath), Times.Once);
        _ui.Verify(u => u.ConfirmRestoreCredentialStoreBackup(), Times.Once);
        _databaseService.Verify(d => d.LoadCredentialStoreFromPath(backupPath), Times.Once);
        _ui.Verify(u => u.PromptVerifyPin(It.IsAny<CredentialStore>(), It.IsAny<byte[]?>()), Times.Never);
        _ui.Verify(u => u.PromptNewPin(), Times.Once);
        _ui.Verify(u => u.ShowError(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void FileNotFound_BackupLoadFails_ShowsErrorWithoutRetryLoop()
    {
        var backupPath = _credentialsFilePath + ".lastgood";
        _databaseService.Setup(d => d.LoadCredentialStore()).Throws<FileNotFoundException>();
        _loadedGoodBackupStore.Setup(b => b.Exists(_credentialsFilePath)).Returns(true);
        _databaseService
            .Setup(d => d.LoadCredentialStoreFromPath(backupPath))
            .Throws(new IOException("backup failed"));
        _ui.Setup(u => u.ConfirmRestoreCredentialStoreBackup()).Returns(true);

        var loader = BuildLoader();

        var result = loader.LoadAndVerifyCredentials(_selectedConfigPath);

        Assert.Null(result);
        _loadedGoodBackupStore.Verify(b => b.Exists(_credentialsFilePath), Times.Once);
        _ui.Verify(u => u.ConfirmRestoreCredentialStoreBackup(), Times.Once);
        _databaseService.Verify(d => d.LoadCredentialStoreFromPath(backupPath), Times.Once);
        _databaseService.Verify(d => d.LoadCredentialStore(), Times.Once);
        _ui.Verify(u => u.PromptNewPin(), Times.Never);
        _ui.Verify(u => u.ShowError(It.Is<string>(s => s.Contains("backup failed")), It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public void FileNotFound_PromptNewPinCancelled_ReturnsNull()
    {
        _databaseService.Setup(d => d.LoadCredentialStore()).Throws<FileNotFoundException>();
        _ui.Setup(u => u.PromptNewPin()).Returns((PinResetResult?)null);

        var loader = BuildLoader();

        var result = loader.LoadAndVerifyCredentials(_selectedConfigPath);

        Assert.Null(result);
        _ui.Verify(u => u.PromptNewPin(), Times.Once);
    }

    [Fact]
    public void JsonException_BackupExistsAndRestoreConfirmed_LoadsBackupDirectlyAndContinuesPinVerification()
    {
        var backupPath = _credentialsFilePath + ".lastgood";
        _databaseService.Setup(d => d.LoadCredentialStore())
            .Throws(new JsonException("corrupt"));
        _databaseService
            .Setup(d => d.LoadCredentialStoreFromPath(backupPath))
            .Returns(_store);
        _rememberPinService.Setup(a => a.IsEnabled).Returns(false);
        _loadedGoodBackupStore.Setup(b => b.Exists(_credentialsFilePath)).Returns(true);
        _ui.Setup(u => u.ConfirmRestoreCredentialStoreBackup()).Returns(true);
        _ui.Setup(u => u.PromptVerifyPin(_store, null))
            .Returns(PinVerifyOutcome.Verified(CreateSecret(_pinDerivedKey), null));

        var loader = BuildLoader();

        using var result = loader.LoadAndVerifyCredentials(_selectedConfigPath);

        Assert.NotNull(result);
        Assert.Equal(_store, result!.Store);
        _loadedGoodBackupStore.Verify(b => b.Exists(_credentialsFilePath), Times.Once);
        _ui.Verify(u => u.ConfirmRestoreCredentialStoreBackup(), Times.Once);
        _databaseService.Verify(d => d.LoadCredentialStoreFromPath(backupPath), Times.Once);
        _databaseService.Verify(d => d.SaveCredentialStore(_store), Times.Once);
        _loadedGoodBackupStore.Verify(
            b => b.TryPreserveCurrentFile(It.IsAny<string>(), out It.Ref<string?>.IsAny),
            Times.Never);
        _ui.Verify(u => u.PromptVerifyPin(_store, null), Times.Once);
        _ui.Verify(u => u.PromptNewPin(), Times.Never);
        _ui.Verify(u => u.ShowError(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void JsonException_BackupExistsButRestoreDeclined_ShowsCorruptionError()
    {
        _databaseService.Setup(d => d.LoadCredentialStore()).Throws(new JsonException("corrupt"));
        _loadedGoodBackupStore.Setup(b => b.Exists(_credentialsFilePath)).Returns(true);
        _ui.Setup(u => u.ConfirmRestoreCredentialStoreBackup()).Returns(false);

        var loader = BuildLoader();

        var result = loader.LoadAndVerifyCredentials(_selectedConfigPath);

        Assert.Null(result);
        _loadedGoodBackupStore.Verify(b => b.Exists(_credentialsFilePath), Times.Once);
        _ui.Verify(u => u.ConfirmRestoreCredentialStoreBackup(), Times.Once);
        _databaseService.Verify(d => d.LoadCredentialStoreFromPath(It.IsAny<string>()), Times.Never);
        _ui.Verify(u => u.PromptNewPin(), Times.Never);
        _ui.Verify(u => u.ShowError(It.Is<string>(s => s.Contains("corrupt")), It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public void JsonException_BackupStoreStillCorrupt_BackupAttemptRemainsSingleShotAndShowsError()
    {
        var backupPath = _credentialsFilePath + ".lastgood";
        _databaseService.Setup(d => d.LoadCredentialStore())
            .Throws(new JsonException("corrupt"));
        _databaseService
            .Setup(d => d.LoadCredentialStoreFromPath(backupPath))
            .Throws(new JsonException("still corrupt"));
        _loadedGoodBackupStore.Setup(b => b.Exists(_credentialsFilePath)).Returns(true);
        _ui.Setup(u => u.ConfirmRestoreCredentialStoreBackup()).Returns(true);

        var loader = BuildLoader();

        var result = loader.LoadAndVerifyCredentials(_selectedConfigPath);

        Assert.Null(result);
        _loadedGoodBackupStore.Verify(b => b.Exists(_credentialsFilePath), Times.Once);
        _ui.Verify(u => u.ConfirmRestoreCredentialStoreBackup(), Times.Once);
        _databaseService.Verify(d => d.LoadCredentialStoreFromPath(backupPath), Times.Once);
        _ui.Verify(u => u.PromptVerifyPin(It.IsAny<CredentialStore>(), It.IsAny<byte[]?>()), Times.Never);
        _ui.Verify(u => u.PromptNewPin(), Times.Never);
        _ui.Verify(u => u.ShowError(It.Is<string>(s => s.Contains("corrupt")), It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public void JsonException_BackupLoadFails_ShowsCorruptionError()
    {
        var backupPath = _credentialsFilePath + ".lastgood";
        _databaseService.Setup(d => d.LoadCredentialStore()).Throws(new JsonException("corrupt"));
        _loadedGoodBackupStore.Setup(b => b.Exists(_credentialsFilePath)).Returns(true);
        _databaseService
            .Setup(d => d.LoadCredentialStoreFromPath(backupPath))
            .Throws(new IOException("backup failed"));
        _ui.Setup(u => u.ConfirmRestoreCredentialStoreBackup()).Returns(true);

        var loader = BuildLoader();

        var result = loader.LoadAndVerifyCredentials(_selectedConfigPath);

        Assert.Null(result);
        _loadedGoodBackupStore.Verify(b => b.Exists(_credentialsFilePath), Times.Once);
        _ui.Verify(u => u.ConfirmRestoreCredentialStoreBackup(), Times.Once);
        _databaseService.Verify(d => d.LoadCredentialStoreFromPath(backupPath), Times.Once);
        _databaseService.Verify(d => d.LoadCredentialStore(), Times.Once);
        _ui.Verify(u => u.PromptNewPin(), Times.Never);
        _ui.Verify(u => u.ShowError(It.Is<string>(s => s.Contains("backup failed")), It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public void JsonException_ShowsErrorReturnsNull()
    {
        _databaseService.Setup(d => d.LoadCredentialStore()).Throws(new JsonException("corrupt"));

        var loader = BuildLoader();

        var result = loader.LoadAndVerifyCredentials(_selectedConfigPath);

        Assert.Null(result);
        _ui.Verify(u => u.ShowError(It.Is<string>(s => s.Contains("corrupt")), It.IsAny<string>()), Times.Once);
        _log.Verify(l => l.Error(It.IsAny<string>(), It.IsAny<Exception>()), Times.Once);
    }

    [Fact]
    public void IOException_ShowsErrorReturnsNull()
    {
        _databaseService.Setup(d => d.LoadCredentialStore()).Throws(new IOException("access denied"));

        var loader = BuildLoader();

        var result = loader.LoadAndVerifyCredentials(_selectedConfigPath);

        Assert.Null(result);
        _ui.Verify(u => u.ShowError(It.Is<string>(s => s.Contains("access denied")), It.IsAny<string>()), Times.Once);
        _log.Verify(l => l.Error(It.IsAny<string>(), It.IsAny<Exception>()), Times.Once);
    }

    [Fact]
    public void DpapiLoss_PromptsRecoveryPin()
    {
        var storeWithCred = BuildStoreWithCredential();
        _databaseService.Setup(d => d.LoadCredentialStore())
            .Returns(storeWithCred);
        _rememberPinService.Setup(a => a.IsEnabled).Returns(false);
        _ui.Setup(u => u.PromptVerifyPin(storeWithCred, null))
            .Returns(PinVerifyOutcome.Verified(CreateSecret(_pinDerivedKey), null));
        _encryptionService.EnqueueDecrypt(() => throw new CryptographicException("DPAPI loss"));

        var recoveryStore = new CredentialStore();
        var recoveryKey = new byte[32];
        new Random(2).NextBytes(recoveryKey);
        _ui.Setup(u => u.PromptRecoveryPin(null))
            .Returns(new RecoveryPinOutcome(recoveryStore, CreateSecret(recoveryKey), null));

        var loader = BuildLoader();

        using var result = loader.LoadAndVerifyCredentials(_selectedConfigPath);

        Assert.NotNull(result);
        Assert.Equal(recoveryStore, result!.Store);
        using var pinKey = result.TakePinDerivedKey();
        AssertSecretEqual(recoveryKey, pinKey);
        _ui.Verify(u => u.PromptRecoveryPin(null), Times.Once);
        _loadedGoodBackupStore.Verify(
            b => b.TryPreserveCurrentFile(It.IsAny<string>(), out It.Ref<string?>.IsAny),
            Times.Never);
        _log.Verify(l => l.Error(It.Is<string>(s => s.Contains("DPAPI")), It.IsAny<Exception>()), Times.Once);
    }

    [Fact]
    public void DpapiLoss_DeletesCredentialFileAndPassesOriginalConfigSaltToRecovery()
    {
        var storeWithCred = BuildStoreWithCredential();
        var configSalt = new byte[32];
        new Random(6).NextBytes(configSalt);
        File.WriteAllBytes(_credentialsFilePath, [0xAA, 0xBB, 0xCC]);

        _databaseService.Setup(d => d.LoadCredentialStore())
            .Returns(storeWithCred);
        _databaseService.Setup(d => d.TryGetConfigSaltFromPath(_selectedConfigPath)).Returns(configSalt);
        _rememberPinService.Setup(a => a.IsEnabled).Returns(false);
        _ui.Setup(u => u.PromptVerifyPin(storeWithCred, It.Is<byte[]?>(salt => salt != null && salt.SequenceEqual(configSalt))))
            .Returns(PinVerifyOutcome.Verified(CreateSecret(_pinDerivedKey), null));
        _encryptionService.EnqueueDecrypt(() => throw new CryptographicException("DPAPI loss"));

        var recoveryStore = new CredentialStore();
        var recoveryKey = new byte[32];
        byte[]? capturedRecoverySalt = null;
        _ui.Setup(u => u.PromptRecoveryPin(It.IsAny<byte[]?>()))
            .Callback<byte[]?>(salt => capturedRecoverySalt = salt == null ? null : (byte[])salt.Clone())
            .Returns(new RecoveryPinOutcome(recoveryStore, CreateSecret(recoveryKey), null));

        var loader = BuildLoader();

        using var result = loader.LoadAndVerifyCredentials(_selectedConfigPath);

        Assert.NotNull(result);
        Assert.False(File.Exists(_credentialsFilePath));
        Assert.NotNull(capturedRecoverySalt);
        Assert.Equal(configSalt, capturedRecoverySalt);
    }

    [Fact]
    public void DpapiLoss_ClassifiesAllEncryptedCredentialsBeforeRecovery()
    {
        var storeWithCreds = new CredentialStore
        {
            ArgonSalt = _store.ArgonSalt,
            EncryptedCanary = _store.EncryptedCanary,
            Credentials =
            [
                new CredentialEntry { Sid = "S-1-5-21-999-999-999-1001", EncryptedPassword = [1, 2, 3, 4] },
                new CredentialEntry { Sid = "S-1-5-21-999-999-999-1002", EncryptedPassword = [5, 6, 7, 8] }
            ]
        };
        _databaseService.Setup(d => d.LoadCredentialStore())
            .Returns(storeWithCreds);
        _rememberPinService.Setup(a => a.IsEnabled).Returns(false);
        _ui.Setup(u => u.PromptVerifyPin(storeWithCreds, null))
            .Returns(PinVerifyOutcome.Verified(CreateSecret(_pinDerivedKey), null));
        _encryptionService.EnqueueDecrypt(() => throw new CryptographicException("DPAPI loss"));
        _encryptionService.EnqueueDecrypt(() => ProtectedString.FromChars("ok".AsSpan()));

        var recoveryStore = new CredentialStore();
        var recoveryKey = new byte[32];
        _ui.Setup(u => u.PromptRecoveryPin(null))
            .Returns(new RecoveryPinOutcome(recoveryStore, CreateSecret(recoveryKey), null));

        var loader = BuildLoader();

        using var result = loader.LoadAndVerifyCredentials(_selectedConfigPath);

        Assert.NotNull(result);
        Assert.Equal(2, _encryptionService.DecryptCallCount);
        _ui.Verify(u => u.PromptRecoveryPin(null), Times.Once);
    }

    [Fact]
    public void DpapiLoss_RecoveryPinCancelled_ReturnsNull()
    {
        var storeWithCred = BuildStoreWithCredential();
        _databaseService.Setup(d => d.LoadCredentialStore())
            .Returns(storeWithCred);
        _rememberPinService.Setup(a => a.IsEnabled).Returns(false);
        _ui.Setup(u => u.PromptVerifyPin(storeWithCred, null))
            .Returns(PinVerifyOutcome.Verified(CreateSecret(_pinDerivedKey), null));
        _encryptionService.EnqueueDecrypt(() => throw new CryptographicException("DPAPI loss"));
        _ui.Setup(u => u.PromptRecoveryPin(null)).Returns((RecoveryPinOutcome?)null);

        var loader = BuildLoader();

        var result = loader.LoadAndVerifyCredentials(_selectedConfigPath);

        Assert.Null(result);
        _ui.Verify(u => u.PromptRecoveryPin(null), Times.Once);
    }

    [Fact]
    public void PinCancelled_ReturnsNull()
    {
        _rememberPinService.Setup(a => a.IsEnabled).Returns(false);
        _ui.Setup(u => u.PromptVerifyPin(_store, null))
            .Returns(PinVerifyOutcome.Canceled());

        var loader = BuildLoader();

        var result = loader.LoadAndVerifyCredentials(_selectedConfigPath);

        Assert.Null(result);
        _ui.Verify(u => u.PromptVerifyPin(_store, null), Times.Once);
        _loadedGoodBackupStore.Verify(
            b => b.TryPreserveCurrentFile(It.IsAny<string>(), out It.Ref<string?>.IsAny),
            Times.Never);
    }

    [Fact]
    public void PinReset_ReturnsNewStore()
    {
        _rememberPinService.Setup(a => a.IsEnabled).Returns(false);

        var resetStore = new CredentialStore();
        var resetKey = new byte[32];
        new Random(3).NextBytes(resetKey);

        _ui.Setup(u => u.PromptVerifyPin(_store, null))
            .Returns(PinVerifyOutcome.Reset(resetStore, CreateSecret(resetKey)));

        var loader = BuildLoader();

        using var result = loader.LoadAndVerifyCredentials(_selectedConfigPath);

        Assert.NotNull(result);
        Assert.Equal(resetStore, result!.Store);
        using var pinKey = result.TakePinDerivedKey();
        AssertSecretEqual(resetKey, pinKey);
        _log.Verify(l => l.Info(It.Is<string>(s => s.Contains("PIN reset"))), Times.Once);
    }

    [Fact]
    public void SaltMismatch_PassesMismatchSalt()
    {
        var differentSalt = new byte[32];
        new Random(4).NextBytes(differentSalt);
        _databaseService.Setup(d => d.TryGetConfigSaltFromPath(_selectedConfigPath)).Returns(differentSalt);

        _rememberPinService.Setup(a => a.IsEnabled).Returns(false);

        byte[]? capturedConfigSalt = null;
        _ui.Setup(u => u.PromptVerifyPin(_store, It.IsAny<byte[]?>()))
            .Callback<CredentialStore, byte[]?>((_, salt) => capturedConfigSalt = salt)
            .Returns(PinVerifyOutcome.Verified(CreateSecret(_pinDerivedKey), null));

        var loader = BuildLoader();

        using var result = loader.LoadAndVerifyCredentials(_selectedConfigPath);

        Assert.NotNull(result);
        Assert.NotNull(capturedConfigSalt);
        Assert.Equal(differentSalt, capturedConfigSalt!);
    }

    [Fact]
    public void SaltMismatch_RememberPinValid_DoesNotBypassAndStillPromptsForPinWithMismatchSalt()
    {
        var differentSalt = new byte[32];
        new Random(5).NextBytes(differentSalt);
        _databaseService.Setup(d => d.TryGetConfigSaltFromPath(_selectedConfigPath)).Returns(differentSalt);

        _rememberPinService.Setup(a => a.IsEnabled).Returns(true);
        _rememberPinService.Setup(a => a.TryDecryptSecret(out It.Ref<SecureSecret?>.IsAny))
            .Returns((out SecureSecret? key) =>
            {
                key = CreateSecret(_pinDerivedKey);
                return true;
            });

        _ui.Setup(u => u.PromptVerifyPin(_store, differentSalt))
            .Returns(PinVerifyOutcome.Verified(CreateSecret(_pinDerivedKey), null));

        var loader = BuildLoader();

        using var result = loader.LoadAndVerifyCredentials(_selectedConfigPath);

        Assert.NotNull(result);
        Assert.False(result!.PinBypassed);
        _ui.Verify(u => u.PromptVerifyPin(_store, differentSalt), Times.Once);
    }

    [Fact]
    public void LoadAndVerifyCredentials_PrimaryAcceptedStore_DoesNotPreserveLoadedGoodBackupByItself()
    {
        _rememberPinService.Setup(a => a.IsEnabled).Returns(false);
        _ui.Setup(u => u.PromptVerifyPin(_store, null))
            .Returns(PinVerifyOutcome.Verified(CreateSecret(_pinDerivedKey), null));

        var loader = BuildLoader();

        using var result = loader.LoadAndVerifyCredentials(_selectedConfigPath);

        Assert.NotNull(result);
        _loadedGoodBackupStore.Verify(
            b => b.TryPreserveCurrentFile(It.IsAny<string>(), out It.Ref<string?>.IsAny),
            Times.Never);
    }

    [Fact]
    public void LoadAndVerifyCredentials_PrimaryAcceptedStore_DoesNotLogLoadedGoodBackupWarning()
    {
        _rememberPinService.Setup(a => a.IsEnabled).Returns(false);
        _ui.Setup(u => u.PromptVerifyPin(_store, null))
            .Returns(PinVerifyOutcome.Verified(CreateSecret(_pinDerivedKey), null));

        var loader = BuildLoader();

        using var result = loader.LoadAndVerifyCredentials(_selectedConfigPath);

        Assert.NotNull(result);
        _loadedGoodBackupStore.Verify(
            b => b.TryPreserveCurrentFile(It.IsAny<string>(), out It.Ref<string?>.IsAny),
            Times.Never);
        _log.Verify(l => l.Warn(It.Is<string>(s => s.Contains("preserve warning"))), Times.Never);
    }

    private sealed class FakeCredentialEncryptionSpanService : ICredentialEncryptionSpanService
    {
        private readonly Queue<Func<ProtectedString>> _decryptSteps = new();

        public int DecryptCallCount { get; private set; }

        public void EnqueueDecrypt(Func<ProtectedString> step) => _decryptSteps.Enqueue(step);

        public byte[] Encrypt(ProtectedString password, ReadOnlySpan<byte> pinDerivedKey)
            => throw new NotSupportedException();

        public ProtectedString Decrypt(byte[] encryptedPassword, ReadOnlySpan<byte> pinDerivedKey)
        {
            DecryptCallCount++;
            if (_decryptSteps.Count == 0)
                throw new InvalidOperationException("No fake decrypt step was configured.");

            return _decryptSteps.Dequeue()();
        }
    }
}
