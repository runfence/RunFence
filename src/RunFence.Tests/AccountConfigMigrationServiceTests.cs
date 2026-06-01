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
    private readonly TestCredentialEncryptionService _encryptionService = new();
    private readonly Mock<IUserImpersonationHelper> _impersonationHelper = new();
    private readonly Mock<IConfigPaths> _configPaths = new();
    private readonly ILoadedGoodBackupStore _loadedGoodBackupStore = new LoadedGoodBackupStore(
        new PersistenceAtomicFileWriter(new PersistenceFileSecurityMirror()),
        new PersistenceFileSecurityMirror());
    private readonly Mock<ILoggingService> _log = new();

    private readonly string _tempDir;
    private readonly string _configFilePath;
    private readonly string _credentialsFilePath;
    private readonly string _licenseFilePath;
    private readonly string _startKeyFilePath;
    private readonly byte[] _pinKey = new byte[32];
    private readonly SecureSecret _pinKeySource;

    public AccountConfigMigrationServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"RunFenceTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        _configFilePath = Path.Combine(_tempDir, "config.dat");
        _credentialsFilePath = Path.Combine(_tempDir, "credentials.dat");
        _licenseFilePath = Path.Combine(_tempDir, "license.dat");
        _startKeyFilePath = Path.Combine(_tempDir, "startkey.dat");
        File.WriteAllText(_configFilePath, "config-content");
        File.WriteAllText(_credentialsFilePath, "credentials-content");

        _configPaths.Setup(p => p.ConfigFilePath).Returns(_configFilePath);
        _configPaths.Setup(p => p.CredentialsFilePath).Returns(_credentialsFilePath);
        _configPaths.Setup(p => p.LicenseFilePath).Returns(_licenseFilePath);
        _configPaths.Setup(p => p.RememberPinFilePath).Returns(_startKeyFilePath);

        new Random(42).NextBytes(_pinKey);
        _pinKeySource = TestSecretFactory.FromBytes(_pinKey);
    }

    public void Dispose()
    {
        _pinKeySource.Dispose();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private AccountConfigMigrationService CreateService()
        => new(
            _profilePathResolver.Object,
            _encryptionService,
            _impersonationHelper.Object,
            _configPaths.Object,
            new ManagedPersistenceFileCleaner(_loadedGoodBackupStore, _log.Object),
            _log.Object);

    private static ProtectedString MakePassword(string value = "pw")
        => new(value.AsSpan(), protect: false);

    private void SetupImpersonation(string profilePath)
    {
        _impersonationHelper
            .Setup(h => h.RunImpersonated<CredentialStore>(
                It.IsAny<string>(), It.IsAny<ProtectedString>(), It.IsAny<Func<CredentialStore>>()))
            .Returns<string, ProtectedString, Func<CredentialStore>>((_, _, action) => (profilePath, action()));
    }

    [Fact]
    public void TargetHasExistingData_ReturnsFalse_WhenNoProfile()
    {
        _profilePathResolver.Setup(r => r.TryGetProfilePath(TargetSid)).Returns((string?)null);

        Assert.False(CreateService().TargetHasExistingData(TargetSid));
    }

    [Fact]
    public void TargetHasExistingData_ReturnsFalse_WhenNoRunFenceFiles()
    {
        var profileDir = Path.Combine(_tempDir, "profile");
        Directory.CreateDirectory(profileDir);
        _profilePathResolver.Setup(r => r.TryGetProfilePath(TargetSid)).Returns(profileDir);

        Assert.False(CreateService().TargetHasExistingData(TargetSid));
    }

    [Theory]
    [InlineData(@"AppData\Local\RunFence\credentials.dat")]
    [InlineData(@"AppData\Roaming\RunFence\config.dat")]
    public void TargetHasExistingData_ReturnsTrue_WhenRunFenceFileExists(string relativeFilePath)
    {
        var profileDir = Path.Combine(_tempDir, "profile");
        var fullPath = Path.Combine(profileDir, relativeFilePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, "data");
        _profilePathResolver.Setup(r => r.TryGetProfilePath(TargetSid)).Returns(profileDir);

        Assert.True(CreateService().TargetHasExistingData(TargetSid));
    }

    [Fact]
    public void MigrateToAccount_DecryptsBeforeImpersonation_EncryptsDuring()
    {
        var callOrder = new List<string>();
        var encryptedPw = new byte[] { 0x01, 0x02 };
        var decryptedPw = MakePassword("secret");
        var reEncryptedBytes = new byte[] { 0x03, 0x04 };

        _encryptionService.OnDecrypt = (ciphertext, key) =>
        {
            Assert.Equal(encryptedPw, ciphertext);
            Assert.Equal(_pinKey, key);
            callOrder.Add("Decrypt");
            return decryptedPw;
        };
        _encryptionService.OnEncrypt = (password, key) =>
        {
            Assert.Same(decryptedPw, password);
            Assert.Equal(_pinKey, key);
            callOrder.Add("Encrypt");
            return reEncryptedBytes;
        };

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
        CreateService().MigrateToAccount(store, TargetSid, targetPassword, _pinKeySource);

        Assert.Equal(["Decrypt", "ImpersonationStart", "Encrypt", "ImpersonationEnd"], callOrder);
    }

    [Fact]
    public void MigrateToAccount_IsCurrentAccount_WithPassword_ReEncrypts()
    {
        var currentSid = SidResolutionHelper.GetCurrentUserSid();
        var encryptedPw = new byte[] { 0x01 };
        var decryptedPw = MakePassword("current-account-secret");
        var reEncryptedPw = new byte[] { 0x02 };
        _encryptionService.OnDecrypt = (_, _) => decryptedPw;
        _encryptionService.OnEncrypt = (_, _) => reEncryptedPw;

        var profileDir = Path.Combine(_tempDir, "target-profile");
        SetupImpersonation(profileDir);

        var store = new CredentialStore();
        store.Credentials.Add(new CredentialEntry { Sid = currentSid, EncryptedPassword = encryptedPw });

        using var targetPassword = MakePassword("targetpw");
        CreateService().MigrateToAccount(store, TargetSid, targetPassword, _pinKeySource);

        Assert.Single(_encryptionService.DecryptCalls);
        var credPath = Path.Combine(profileDir, @"AppData\Local\RunFence\credentials.dat");
        var written = JsonSerializer.Deserialize<CredentialStore>(File.ReadAllText(credPath), JsonDefaults.Options)!;
        Assert.Single(written.Credentials);
        Assert.Equal(reEncryptedPw, written.Credentials[0].EncryptedPassword);
    }

    [Fact]
    public void MigrateToAccount_IsCurrentAccount_WithoutPassword_IsExcluded()
    {
        var currentSid = SidResolutionHelper.GetCurrentUserSid();
        var profileDir = Path.Combine(_tempDir, "target-profile");
        SetupImpersonation(profileDir);

        var store = new CredentialStore();
        store.Credentials.Add(new CredentialEntry { Sid = currentSid, EncryptedPassword = [] });

        using var targetPassword = MakePassword("targetpw");
        CreateService().MigrateToAccount(store, TargetSid, targetPassword, _pinKeySource);

        Assert.Empty(_encryptionService.DecryptCalls);
        Assert.Empty(_encryptionService.EncryptCalls);
        var credPath = Path.Combine(profileDir, @"AppData\Local\RunFence\credentials.dat");
        var written = JsonSerializer.Deserialize<CredentialStore>(File.ReadAllText(credPath), JsonDefaults.Options)!;
        Assert.Empty(written.Credentials);
    }

    [Fact]
    public void MigrateToAccount_CopiesEmptyPasswordEntries_WithoutDecryption()
    {
        var profileDir = Path.Combine(_tempDir, "target-profile");
        SetupImpersonation(profileDir);

        var entryId = Guid.NewGuid();
        var store = new CredentialStore();
        store.Credentials.Add(new CredentialEntry
        {
            Id = entryId,
            Sid = OtherSid,
            EncryptedPassword = []
        });

        using var targetPassword = MakePassword("targetpw");
        CreateService().MigrateToAccount(store, TargetSid, targetPassword, _pinKeySource);

        Assert.Empty(_encryptionService.DecryptCalls);
        Assert.Empty(_encryptionService.EncryptCalls);
        var credPath = Path.Combine(profileDir, @"AppData\Local\RunFence\credentials.dat");
        var written = JsonSerializer.Deserialize<CredentialStore>(File.ReadAllText(credPath), JsonDefaults.Options)!;
        Assert.Single(written.Credentials);
        Assert.Equal(entryId, written.Credentials[0].Id);
        Assert.Equal(OtherSid, written.Credentials[0].Sid, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void MigrateToAccount_WritesFilesToTargetProfile()
    {
        var profileDir = Path.Combine(_tempDir, "target-profile");
        SetupImpersonation(profileDir);

        var store = new CredentialStore
        {
            ArgonSalt = new byte[Constants.Argon2SaltSize],
            EncryptedCanary = [0xAB, 0xCD]
        };

        using var targetPassword = MakePassword("targetpw");
        CreateService().MigrateToAccount(store, TargetSid, targetPassword, _pinKeySource);

        Assert.True(File.Exists(Path.Combine(profileDir, @"AppData\Local\RunFence\credentials.dat")));
        Assert.True(File.Exists(Path.Combine(profileDir, @"AppData\Roaming\RunFence\config.dat")));
    }

    [Fact]
    public void MigrateToAccount_WrittenCredentials_ContainArgonSaltAndCanary()
    {
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
        CreateService().MigrateToAccount(store, TargetSid, targetPassword, _pinKeySource);

        var credPath = Path.Combine(profileDir, @"AppData\Local\RunFence\credentials.dat");
        var written = JsonSerializer.Deserialize<CredentialStore>(File.ReadAllText(credPath), JsonDefaults.Options)!;
        Assert.Equal(argonSalt, written.ArgonSalt);
        Assert.Equal(canary, written.EncryptedCanary);
    }

    [Fact]
    public void MigrateToAccount_ConfigFileIsCopiedToTarget()
    {
        var profileDir = Path.Combine(_tempDir, "target-profile");
        SetupImpersonation(profileDir);
        File.WriteAllText(_configFilePath, "encrypted-config-bytes");

        using var targetPassword = MakePassword("targetpw");
        CreateService().MigrateToAccount(new CredentialStore(), TargetSid, targetPassword, _pinKeySource);

        var targetConfigPath = Path.Combine(profileDir, @"AppData\Roaming\RunFence\config.dat");
        Assert.Equal("encrypted-config-bytes", File.ReadAllText(targetConfigPath));
    }

    [Fact]
    public void MigrateToAccount_CopiesLicenseFile_WhenExists()
    {
        var profileDir = Path.Combine(_tempDir, "target-profile");
        SetupImpersonation(profileDir);
        File.WriteAllText(_licenseFilePath, "license-data");

        try
        {
            using var targetPassword = MakePassword("targetpw");
            CreateService().MigrateToAccount(new CredentialStore(), TargetSid, targetPassword, _pinKeySource);

            var targetLicensePath = Path.Combine(profileDir, @"AppData\Roaming\RunFence\license.dat");
            Assert.True(File.Exists(targetLicensePath));
            Assert.Equal("license-data", File.ReadAllText(targetLicensePath));
        }
        finally
        {
            if (File.Exists(_licenseFilePath))
                File.Delete(_licenseFilePath);
        }
    }

    [Fact]
    public void MigrateToAccount_SkipsLicenseFile_WhenNotExists()
    {
        if (File.Exists(_licenseFilePath))
            File.Delete(_licenseFilePath);

        var profileDir = Path.Combine(_tempDir, "target-profile");
        SetupImpersonation(profileDir);

        using var targetPassword = MakePassword("targetpw");
        CreateService().MigrateToAccount(new CredentialStore(), TargetSid, targetPassword, _pinKeySource);

        Assert.False(File.Exists(Path.Combine(profileDir, @"AppData\Roaming\RunFence\license.dat")));
    }

    [Fact]
    public void MigrateToAccount_ReEncryptsPasswordsUnderTargetIdentity()
    {
        var encryptedPw = new byte[] { 0x01, 0x02, 0x03 };
        var decryptedPw = MakePassword("mysecret");
        var reEncryptedPw = new byte[] { 0x10, 0x20, 0x30 };
        _encryptionService.OnDecrypt = (_, _) => decryptedPw;
        _encryptionService.OnEncrypt = (_, _) => reEncryptedPw;

        var profileDir = Path.Combine(_tempDir, "target-profile");
        SetupImpersonation(profileDir);

        var store = new CredentialStore();
        store.Credentials.Add(new CredentialEntry { Sid = OtherSid, EncryptedPassword = encryptedPw });

        using var targetPassword = MakePassword("targetpw");
        CreateService().MigrateToAccount(store, TargetSid, targetPassword, _pinKeySource);

        var credPath = Path.Combine(profileDir, @"AppData\Local\RunFence\credentials.dat");
        var written = JsonSerializer.Deserialize<CredentialStore>(File.ReadAllText(credPath), JsonDefaults.Options)!;
        Assert.Single(written.Credentials);
        Assert.Equal(reEncryptedPw, written.Credentials[0].EncryptedPassword);
        Assert.Equal(OtherSid, written.Credentials[0].Sid, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void DeleteCurrentAccountData_DeletesAllFiles()
    {
        File.WriteAllText(_licenseFilePath, "lic");
        File.WriteAllText(_startKeyFilePath, "key");

        try
        {
            CreateService().DeleteCurrentAccountData();

            Assert.False(File.Exists(_credentialsFilePath));
            Assert.False(File.Exists(_configFilePath));
            Assert.False(File.Exists(_licenseFilePath));
            Assert.False(File.Exists(_startKeyFilePath));
        }
        finally
        {
            if (File.Exists(_licenseFilePath))
                File.Delete(_licenseFilePath);
        }
    }

    [Fact]
    public void DeleteCurrentAccountData_DeletesLoadedGoodBackupsAndManagedRollbackTempArtifacts()
    {
        File.WriteAllText(_licenseFilePath, "lic");
        File.WriteAllText(_startKeyFilePath, "key");

        var deletedPaths = new[]
        {
            _credentialsFilePath,
            _configFilePath,
            _licenseFilePath,
            _startKeyFilePath
        };

        foreach (var primaryFilePath in deletedPaths)
        {
            File.WriteAllText(_loadedGoodBackupStore.GetBackupPath(primaryFilePath), "lastgood");
            File.WriteAllText(primaryFilePath + ".12345678.rollback", "rollback");
            File.WriteAllText(primaryFilePath + ".87654321.tmp", "tmp");
        }

        var preservedSiblingFiles = new[]
        {
            _credentialsFilePath + ".lastgood.keep",
            _configFilePath + "x.rollback",
            _licenseFilePath + ".tmp.keep",
            _startKeyFilePath + "x.12345678.tmp",
            Path.Combine(_tempDir, "other.dat.12345678.rollback"),
            Path.Combine(_tempDir, "other.dat.87654321.tmp")
        };

        foreach (var preservedPath in preservedSiblingFiles)
            File.WriteAllText(preservedPath, "keep");

        var nestedManagedArtifact = Path.Combine(_tempDir, "nested", Path.GetFileName(_credentialsFilePath) + ".12345678.rollback");
        Directory.CreateDirectory(Path.GetDirectoryName(nestedManagedArtifact)!);
        File.WriteAllText(nestedManagedArtifact, "keep");

        CreateService().DeleteCurrentAccountData();

        foreach (var primaryFilePath in deletedPaths)
        {
            Assert.False(File.Exists(_loadedGoodBackupStore.GetBackupPath(primaryFilePath)));
            Assert.False(File.Exists(primaryFilePath + ".12345678.rollback"));
            Assert.False(File.Exists(primaryFilePath + ".87654321.tmp"));
        }

        foreach (var preservedPath in preservedSiblingFiles)
            Assert.True(File.Exists(preservedPath));

        Assert.True(File.Exists(nestedManagedArtifact));
    }

    [Fact]
    public void DeleteCurrentAccountData_SkipsMissingFiles()
    {
        File.Delete(_configFilePath);
        File.Delete(_credentialsFilePath);
        if (File.Exists(_licenseFilePath))
            File.Delete(_licenseFilePath);

        CreateService().DeleteCurrentAccountData();
    }

    [Fact]
    public void DeleteCurrentAccountData_DeletesOnlyExistingFiles()
    {
        File.Delete(_configFilePath);
        if (File.Exists(_licenseFilePath))
            File.Delete(_licenseFilePath);

        CreateService().DeleteCurrentAccountData();

        Assert.False(File.Exists(_credentialsFilePath));
        _log.Verify(l => l.Info(It.Is<string>(s => s.Contains("credentials"))), Times.Once);
    }

    [Fact]
    public void MigrateToAccount_WritesDetachedSnapshot()
    {
        var encryptedPw = new byte[] { 0x01, 0x02, 0x03 };
        var decryptedPw = MakePassword("mysecret");
        var reEncryptedPw = new byte[] { 0x10, 0x20, 0x30 };
        var argonSalt = new byte[Constants.Argon2SaltSize];
        var canary = new byte[] { 0x11, 0x22, 0x33 };
        new Random(123).NextBytes(argonSalt);

        var originalArgonSalt = argonSalt.ToArray();
        var originalCanary = canary.ToArray();
        var originalEncryptedPw = encryptedPw.ToArray();

        _encryptionService.OnDecrypt = (_, _) => decryptedPw;
        _encryptionService.OnEncrypt = (_, _) => reEncryptedPw;

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
        CreateService().MigrateToAccount(store, TargetSid, targetPassword, _pinKeySource);

        argonSalt[0] ^= 0xFF;
        canary[0] ^= 0xFF;
        encryptedPw[0] ^= 0xFF;
        store.Credentials[0].Sid = TargetSid;

        var credPath = Path.Combine(profileDir, @"AppData\Local\RunFence\credentials.dat");
        var written = JsonSerializer.Deserialize<CredentialStore>(File.ReadAllText(credPath), JsonDefaults.Options)!;
        Assert.Equal(originalArgonSalt, written.ArgonSalt);
        Assert.Equal(originalCanary, written.EncryptedCanary);
        Assert.Single(written.Credentials);
        Assert.Equal(credentialId, written.Credentials[0].Id);
        Assert.Equal(OtherSid, written.Credentials[0].Sid, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(reEncryptedPw, written.Credentials[0].EncryptedPassword);
        Assert.NotEqual(originalEncryptedPw, written.Credentials[0].EncryptedPassword);
    }
    private sealed class TestCredentialEncryptionService : ICredentialEncryptionSpanService
    {
        public List<byte[]> DecryptCalls { get; } = [];
        public List<(ProtectedString Password, byte[] Key)> EncryptCalls { get; } = [];
        public Func<byte[], byte[], ProtectedString>? OnDecrypt { get; set; }
        public Func<ProtectedString, byte[], byte[]>? OnEncrypt { get; set; }

        public byte[] Encrypt(ProtectedString password, ReadOnlySpan<byte> pinDerivedKey)
        {
            var key = pinDerivedKey.ToArray();
            EncryptCalls.Add((password, key));
            return OnEncrypt?.Invoke(password, key) ?? Array.Empty<byte>();
        }

        public ProtectedString Decrypt(byte[] encryptedPassword, ReadOnlySpan<byte> pinDerivedKey)
        {
            var key = pinDerivedKey.ToArray();
            DecryptCalls.Add(encryptedPassword.ToArray());
            return OnDecrypt?.Invoke(encryptedPassword, key)
                ?? throw new InvalidOperationException("OnDecrypt was not configured.");
        }
    }
}
