using System.Text.Json;
using Moq;
using RunFence.Account;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Persistence;
using RunFence.Security;
using Xunit;

namespace RunFence.Tests;

public class AccountConfigMigrationServiceTests : IDisposable
{
    private const string TargetSid = "S-1-5-21-9999999999-9999999999-9999999999-2001";
    private const string OtherSid = "S-1-5-21-9999999999-9999999999-9999999999-2002";

    private readonly Mock<IProfilePathResolver> _profilePathResolver = new();
    private readonly Mock<ICredentialEncryptionService> _encryptionService = new();
    private readonly Mock<IUserImpersonationHelper> _impersonationHelper = new();
    private readonly Mock<IConfigPaths> _configPaths = new();
    private readonly Mock<ILoggingService> _log = new();

    private readonly string _tempDir;
    private readonly string _configFilePath;
    private readonly string _credentialsFilePath;
    private readonly string _startKeyFilePath;
    private readonly byte[] _pinKey = new byte[32];

    public AccountConfigMigrationServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"RunFenceTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        _configFilePath = Path.Combine(_tempDir, "config.dat");
        _credentialsFilePath = Path.Combine(_tempDir, "credentials.dat");
        _startKeyFilePath = Path.Combine(_tempDir, "startkey.dat");
        File.WriteAllText(_configFilePath, "config-content");
        File.WriteAllText(_credentialsFilePath, "credentials-content");

        _configPaths.Setup(p => p.ConfigFilePath).Returns(_configFilePath);
        _configPaths.Setup(p => p.CredentialsFilePath).Returns(_credentialsFilePath);
        _configPaths.Setup(p => p.RememberPinFilePath).Returns(_startKeyFilePath);

        new Random(42).NextBytes(_pinKey);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private AccountConfigMigrationService CreateService()
        => new(_profilePathResolver.Object, _encryptionService.Object,
            _impersonationHelper.Object, _configPaths.Object, _log.Object);

    private static ProtectedString MakePassword(string value = "pw")
        => new(value.AsSpan(), protect: false);

    private void SetupImpersonation(string profilePath)
    {
        _impersonationHelper
            .Setup(h => h.RunImpersonated<CredentialStore>(
                It.IsAny<string>(), It.IsAny<ProtectedString>(), It.IsAny<Func<CredentialStore>>()))
            .Returns<string, ProtectedString, Func<CredentialStore>>(
                (_, _, action) => (profilePath, action()));
    }

    // ── TargetHasExistingData ─────────────────────────────────────────────

    [Fact]
    public void TargetHasExistingData_ReturnsFalse_WhenNoProfile()
    {
        // Arrange
        _profilePathResolver.Setup(r => r.TryGetProfilePath(TargetSid)).Returns((string?)null);
        var service = CreateService();

        // Act
        var result = service.TargetHasExistingData(TargetSid);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void TargetHasExistingData_ReturnsFalse_WhenNoRunFenceFiles()
    {
        // Arrange: profile exists but contains no RunFence data files
        var profileDir = Path.Combine(_tempDir, "profile");
        Directory.CreateDirectory(profileDir);
        _profilePathResolver.Setup(r => r.TryGetProfilePath(TargetSid)).Returns(profileDir);
        var service = CreateService();

        // Act
        var result = service.TargetHasExistingData(TargetSid);

        // Assert
        Assert.False(result);
    }

    [Theory]
    [InlineData(@"AppData\Local\RunFence\credentials.dat")]
    [InlineData(@"AppData\Roaming\RunFence\config.dat")]
    public void TargetHasExistingData_ReturnsTrue_WhenRunFenceFileExists(string relativeFilePath)
    {
        // Arrange: one RunFence data file present in the target profile
        var profileDir = Path.Combine(_tempDir, "profile");
        var fullPath = Path.Combine(profileDir, relativeFilePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, "data");
        _profilePathResolver.Setup(r => r.TryGetProfilePath(TargetSid)).Returns(profileDir);
        var service = CreateService();

        // Act
        var result = service.TargetHasExistingData(TargetSid);

        // Assert
        Assert.True(result);
    }

    // ── MigrateToAccount — ordering: decrypt before impersonation, encrypt during ──

    [Fact]
    public void MigrateToAccount_DecryptsBeforeImpersonation_EncryptsDuring()
    {
        // Arrange: track call order to verify Decrypt runs before impersonation, Encrypt runs inside
        var callOrder = new List<string>();

        var encryptedPw = new byte[] { 0x01, 0x02 };
        // Service takes ownership and disposes in finally — do not use `using` here
        var decryptedPw = MakePassword("secret");
        var reEncryptedBytes = new byte[] { 0x03, 0x04 };

        _encryptionService
            .Setup(e => e.Decrypt(encryptedPw, _pinKey))
            .Callback(() => callOrder.Add("Decrypt"))
            .Returns(decryptedPw);

        _encryptionService
            .Setup(e => e.Encrypt(It.IsAny<ProtectedString>(), It.IsAny<byte[]>()))
            .Callback(() => callOrder.Add("Encrypt"))
            .Returns(reEncryptedBytes);

        var profileDir = Path.Combine(_tempDir, "target-profile");
        _impersonationHelper
            .Setup(h => h.RunImpersonated<CredentialStore>(
                It.IsAny<string>(), It.IsAny<ProtectedString>(), It.IsAny<Func<CredentialStore>>()))
            .Returns<string, ProtectedString, Func<CredentialStore>>((_, _, action) =>
            {
                callOrder.Add("ImpersonationStart");
                var result = action();
                callOrder.Add("ImpersonationEnd");
                return (profileDir, result);
            });

        var store = new CredentialStore();
        store.Credentials.Add(new CredentialEntry { Sid = OtherSid, EncryptedPassword = encryptedPw });

        using var targetPassword = MakePassword("targetpw");
        var service = CreateService();

        // Act
        service.MigrateToAccount(store, TargetSid, targetPassword, _pinKey);

        // Assert: Decrypt happens before impersonation starts; Encrypt happens inside impersonation
        Assert.Equal(["Decrypt", "ImpersonationStart", "Encrypt", "ImpersonationEnd"], callOrder);
    }

    [Fact]
    public void MigrateToAccount_IsCurrentAccount_WithPassword_ReEncrypts()
    {
        // Arrange: IsCurrentAccount entry with a stored password — must be re-encrypted on the target
        var currentSid = SidResolutionHelper.GetCurrentUserSid();
        var encryptedPw = new byte[] { 0x01 };
        var decryptedPw = MakePassword("current-account-secret");
        var reEncryptedPw = new byte[] { 0x02 };
        _encryptionService.Setup(e => e.Decrypt(encryptedPw, _pinKey)).Returns(decryptedPw);
        _encryptionService.Setup(e => e.Encrypt(decryptedPw, _pinKey)).Returns(reEncryptedPw);

        var profileDir = Path.Combine(_tempDir, "target-profile");
        SetupImpersonation(profileDir);

        var store = new CredentialStore();
        store.Credentials.Add(new CredentialEntry { Sid = currentSid, EncryptedPassword = encryptedPw });

        using var targetPassword = MakePassword("targetpw");
        var service = CreateService();

        // Act
        service.MigrateToAccount(store, TargetSid, targetPassword, _pinKey);

        // Assert: Decrypt was called and output contains the re-encrypted entry
        _encryptionService.Verify(e => e.Decrypt(encryptedPw, _pinKey), Times.Once);
        var credPath = Path.Combine(profileDir, @"AppData\Local\RunFence\credentials.dat");
        var written = JsonSerializer.Deserialize<CredentialStore>(File.ReadAllText(credPath), JsonDefaults.Options)!;
        Assert.Single(written.Credentials);
        Assert.Equal(reEncryptedPw, written.Credentials[0].EncryptedPassword);
    }

    [Fact]
    public void MigrateToAccount_IsCurrentAccount_WithoutPassword_IsExcluded()
    {
        // Arrange: IsCurrentAccount entry with no password — skipped (target creates its own via EnsureCurrentAccountCredential)
        var currentSid = SidResolutionHelper.GetCurrentUserSid();
        var profileDir = Path.Combine(_tempDir, "target-profile");
        SetupImpersonation(profileDir);

        var store = new CredentialStore();
        store.Credentials.Add(new CredentialEntry { Sid = currentSid, EncryptedPassword = Array.Empty<byte>() });

        using var targetPassword = MakePassword("targetpw");
        var service = CreateService();

        // Act
        service.MigrateToAccount(store, TargetSid, targetPassword, _pinKey);

        // Assert: Decrypt/Encrypt never called; entry absent from output credentials
        _encryptionService.Verify(e => e.Decrypt(It.IsAny<byte[]>(), It.IsAny<byte[]>()), Times.Never);
        _encryptionService.Verify(e => e.Encrypt(It.IsAny<ProtectedString>(), It.IsAny<byte[]>()), Times.Never);
        var credPath = Path.Combine(profileDir, @"AppData\Local\RunFence\credentials.dat");
        var written = JsonSerializer.Deserialize<CredentialStore>(File.ReadAllText(credPath), JsonDefaults.Options)!;
        Assert.Empty(written.Credentials);
    }

    [Fact]
    public void MigrateToAccount_CopiesEmptyPasswordEntries_WithoutDecryption()
    {
        // Arrange: credential with empty EncryptedPassword
        var profileDir = Path.Combine(_tempDir, "target-profile");
        SetupImpersonation(profileDir);

        var entryId = Guid.NewGuid();
        var store = new CredentialStore();
        store.Credentials.Add(new CredentialEntry
        {
            Id = entryId,
            Sid = OtherSid,
            EncryptedPassword = Array.Empty<byte>()
        });

        using var targetPassword = MakePassword("targetpw");
        var service = CreateService();

        // Act
        service.MigrateToAccount(store, TargetSid, targetPassword, _pinKey);

        // Assert: Decrypt/Encrypt never called; entry copied as-is with original Id and Sid
        _encryptionService.Verify(e => e.Decrypt(It.IsAny<byte[]>(), It.IsAny<byte[]>()), Times.Never);
        _encryptionService.Verify(e => e.Encrypt(It.IsAny<ProtectedString>(), It.IsAny<byte[]>()), Times.Never);
        var credPath = Path.Combine(profileDir, @"AppData\Local\RunFence\credentials.dat");
        var written = JsonSerializer.Deserialize<CredentialStore>(File.ReadAllText(credPath), JsonDefaults.Options)!;
        Assert.Single(written.Credentials);
        Assert.Equal(entryId, written.Credentials[0].Id);
        Assert.Equal(OtherSid, written.Credentials[0].Sid, StringComparer.OrdinalIgnoreCase);
    }

    // ── MigrateToAccount — file writes ───────────────────────────────────

    [Fact]
    public void MigrateToAccount_WritesFilesToTargetProfile()
    {
        // Arrange
        var profileDir = Path.Combine(_tempDir, "target-profile");
        SetupImpersonation(profileDir);

        var store = new CredentialStore
        {
            ArgonSalt = new byte[Constants.Argon2SaltSize],
            EncryptedCanary = new byte[] { 0xAB, 0xCD }
        };

        using var targetPassword = MakePassword("targetpw");
        var service = CreateService();

        // Act
        service.MigrateToAccount(store, TargetSid, targetPassword, _pinKey);

        // Assert
        var expectedCredPath = Path.Combine(profileDir, @"AppData\Local\RunFence\credentials.dat");
        var expectedConfigPath = Path.Combine(profileDir, @"AppData\Roaming\RunFence\config.dat");
        Assert.True(File.Exists(expectedCredPath), "credentials.dat should be written to target profile");
        Assert.True(File.Exists(expectedConfigPath), "config.dat should be copied to target profile");
    }

    [Fact]
    public void MigrateToAccount_WrittenCredentials_ContainArgonSaltAndCanary()
    {
        // Arrange
        var profileDir = Path.Combine(_tempDir, "target-profile");
        SetupImpersonation(profileDir);

        var argonSalt = new byte[Constants.Argon2SaltSize];
        new Random(77).NextBytes(argonSalt);
        var canary = new byte[] { 0x11, 0x22, 0x33 };

        var store = new CredentialStore
        {
            ArgonSalt = argonSalt,
            EncryptedCanary = canary
        };

        using var targetPassword = MakePassword("targetpw");
        var service = CreateService();

        // Act
        service.MigrateToAccount(store, TargetSid, targetPassword, _pinKey);

        // Assert: the written credentials.dat contains the same ArgonSalt and EncryptedCanary
        var credPath = Path.Combine(profileDir, @"AppData\Local\RunFence\credentials.dat");
        var json = File.ReadAllText(credPath);
        var written = JsonSerializer.Deserialize<CredentialStore>(json, JsonDefaults.Options)!;
        Assert.Equal(argonSalt, written.ArgonSalt);
        Assert.Equal(canary, written.EncryptedCanary);
    }

    [Fact]
    public void MigrateToAccount_ConfigFileIsCopiedToTarget()
    {
        // Arrange
        var profileDir = Path.Combine(_tempDir, "target-profile");
        SetupImpersonation(profileDir);

        var configContent = "encrypted-config-bytes";
        File.WriteAllText(_configFilePath, configContent);

        var store = new CredentialStore();
        using var targetPassword = MakePassword("targetpw");
        var service = CreateService();

        // Act
        service.MigrateToAccount(store, TargetSid, targetPassword, _pinKey);

        // Assert: config.dat in target contains same content as source
        var targetConfigPath = Path.Combine(profileDir, @"AppData\Roaming\RunFence\config.dat");
        Assert.Equal(configContent, File.ReadAllText(targetConfigPath));
    }

    [Fact]
    public void MigrateToAccount_CopiesLicenseFile_WhenExists()
    {
        // Arrange
        var profileDir = Path.Combine(_tempDir, "target-profile");
        SetupImpersonation(profileDir);

        var licenseContent = "license-data";
        Directory.CreateDirectory(Path.GetDirectoryName(PathConstants.LicenseFilePath)!);
        File.WriteAllText(PathConstants.LicenseFilePath, licenseContent);

        var store = new CredentialStore();
        using var targetPassword = MakePassword("targetpw");
        var service = CreateService();

        try
        {
            // Act
            service.MigrateToAccount(store, TargetSid, targetPassword, _pinKey);

            // Assert
            var targetLicensePath = Path.Combine(profileDir, @"AppData\Roaming\RunFence\license.dat");
            Assert.True(File.Exists(targetLicensePath), "license.dat should be copied to target profile");
            Assert.Equal(licenseContent, File.ReadAllText(targetLicensePath));
        }
        finally
        {
            if (File.Exists(PathConstants.LicenseFilePath))
                File.Delete(PathConstants.LicenseFilePath);
        }
    }

    [Fact]
    public void MigrateToAccount_SkipsLicenseFile_WhenNotExists()
    {
        // Arrange: ensure license file does not exist
        if (File.Exists(PathConstants.LicenseFilePath))
            File.Delete(PathConstants.LicenseFilePath);

        var profileDir = Path.Combine(_tempDir, "target-profile");
        SetupImpersonation(profileDir);

        var store = new CredentialStore();
        using var targetPassword = MakePassword("targetpw");
        var service = CreateService();

        // Act — should not throw even when license file is absent
        service.MigrateToAccount(store, TargetSid, targetPassword, _pinKey);

        // Assert: license.dat not written in target
        var targetLicensePath = Path.Combine(profileDir, @"AppData\Roaming\RunFence\license.dat");
        Assert.False(File.Exists(targetLicensePath));
    }

    [Fact]
    public void MigrateToAccount_ReEncryptsPasswordsUnderTargetIdentity()
    {
        // Arrange
        var encryptedPw = new byte[] { 0x01, 0x02, 0x03 };
        // Service takes ownership and disposes in finally — do not use `using` here
        var decryptedPw = MakePassword("mysecret");
        var reEncryptedPw = new byte[] { 0x10, 0x20, 0x30 };

        _encryptionService.Setup(e => e.Decrypt(encryptedPw, _pinKey)).Returns(decryptedPw);
        _encryptionService.Setup(e => e.Encrypt(decryptedPw, _pinKey)).Returns(reEncryptedPw);

        var profileDir = Path.Combine(_tempDir, "target-profile");
        SetupImpersonation(profileDir);

        var store = new CredentialStore();
        store.Credentials.Add(new CredentialEntry { Sid = OtherSid, EncryptedPassword = encryptedPw });

        using var targetPassword = MakePassword("targetpw");
        var service = CreateService();

        // Act
        service.MigrateToAccount(store, TargetSid, targetPassword, _pinKey);

        // Assert: the written credentials contain re-encrypted password
        var credPath = Path.Combine(profileDir, @"AppData\Local\RunFence\credentials.dat");
        var json = File.ReadAllText(credPath);
        var written = JsonSerializer.Deserialize<CredentialStore>(json, JsonDefaults.Options)!;
        Assert.Single(written.Credentials);
        Assert.Equal(reEncryptedPw, written.Credentials[0].EncryptedPassword);
        Assert.Equal(OtherSid, written.Credentials[0].Sid, StringComparer.OrdinalIgnoreCase);
    }

    // ── DeleteCurrentAccountData ──────────────────────────────────────────

    [Fact]
    public void DeleteCurrentAccountData_DeletesAllFiles()
    {
        // Arrange: all four files exist
        var licenseDir = Path.GetDirectoryName(PathConstants.LicenseFilePath)!;
        Directory.CreateDirectory(licenseDir);
        File.WriteAllText(PathConstants.LicenseFilePath, "lic");
        File.WriteAllText(_startKeyFilePath, "key");
        var service = CreateService();

        try
        {
            // Act
            service.DeleteCurrentAccountData();

            // Assert
            Assert.False(File.Exists(_credentialsFilePath), "credentials.dat should be deleted");
            Assert.False(File.Exists(_configFilePath), "config.dat should be deleted");
            Assert.False(File.Exists(PathConstants.LicenseFilePath), "license.dat should be deleted");
            Assert.False(File.Exists(_startKeyFilePath), "startkey.dat should be deleted");
        }
        finally
        {
            if (File.Exists(PathConstants.LicenseFilePath))
                File.Delete(PathConstants.LicenseFilePath);
        }
    }

    [Fact]
    public void DeleteCurrentAccountData_SkipsMissingFiles()
    {
        // Arrange: none of the files exist
        File.Delete(_configFilePath);
        File.Delete(_credentialsFilePath);
        if (File.Exists(PathConstants.LicenseFilePath))
            File.Delete(PathConstants.LicenseFilePath);

        var service = CreateService();

        // Act — should not throw when files are absent
        service.DeleteCurrentAccountData();
    }

    [Fact]
    public void DeleteCurrentAccountData_DeletesOnlyExistingFiles()
    {
        // Arrange: only credentials exist; config and license absent
        File.Delete(_configFilePath);
        if (File.Exists(PathConstants.LicenseFilePath))
            File.Delete(PathConstants.LicenseFilePath);

        var service = CreateService();

        // Act
        service.DeleteCurrentAccountData();

        // Assert: credentials deleted, no error on missing files
        Assert.False(File.Exists(_credentialsFilePath));
        _log.Verify(l => l.Info(It.Is<string>(s => s.Contains("credentials"))), Times.Once);
    }

    [Fact]
    public void MigrateToAccount_WritesDetachedSnapshot()
    {
        // Arrange: mutating the source after migration must not affect the written store.
        var encryptedPw = new byte[] { 0x01, 0x02, 0x03 };
        var decryptedPw = MakePassword("mysecret");
        var reEncryptedPw = new byte[] { 0x10, 0x20, 0x30 };
        var argonSalt = new byte[Constants.Argon2SaltSize];
        var canary = new byte[] { 0x11, 0x22, 0x33 };
        new Random(123).NextBytes(argonSalt);

        var originalArgonSalt = argonSalt.ToArray();
        var originalCanary = canary.ToArray();
        var originalEncryptedPw = encryptedPw.ToArray();

        _encryptionService.Setup(e => e.Decrypt(encryptedPw, _pinKey)).Returns(decryptedPw);
        _encryptionService.Setup(e => e.Encrypt(decryptedPw, _pinKey)).Returns(reEncryptedPw);

        var profileDir = Path.Combine(_tempDir, "target-profile");
        SetupImpersonation(profileDir);

        var store = new CredentialStore
        {
            ArgonSalt = argonSalt,
            EncryptedCanary = canary
        };
        var credentialId = Guid.NewGuid();
        store.Credentials.Add(new CredentialEntry
        {
            Id = credentialId,
            Sid = OtherSid,
            EncryptedPassword = encryptedPw
        });

        using var targetPassword = MakePassword("targetpw");
        var service = CreateService();

        // Act
        service.MigrateToAccount(store, TargetSid, targetPassword, _pinKey);

        // Mutate the source after migration. The file must keep the original snapshot.
        argonSalt[0] ^= 0xFF;
        canary[0] ^= 0xFF;
        encryptedPw[0] ^= 0xFF;
        store.Credentials[0].Sid = TargetSid;

        // Assert
        var credPath = Path.Combine(profileDir, @"AppData\Local\RunFence\credentials.dat");
        var json = File.ReadAllText(credPath);
        var written = JsonSerializer.Deserialize<CredentialStore>(json, JsonDefaults.Options)!;
        Assert.Equal(originalArgonSalt, written.ArgonSalt);
        Assert.Equal(originalCanary, written.EncryptedCanary);
        Assert.Single(written.Credentials);
        Assert.Equal(credentialId, written.Credentials[0].Id);
        Assert.Equal(OtherSid, written.Credentials[0].Sid, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(reEncryptedPw, written.Credentials[0].EncryptedPassword);
        Assert.NotEqual(originalEncryptedPw, written.Credentials[0].EncryptedPassword);
    }
}
