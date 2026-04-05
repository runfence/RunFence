using System.Security.Cryptography;
using System.Text.Json;
using Moq;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Persistence;
using Xunit;

namespace RunFence.Tests;

public class DatabaseServiceTests : IDisposable
{
    private readonly DatabaseService _service;
    private readonly DatabaseService _plaintextService;
    private readonly TempDirectory _tempDir;
    private readonly byte[] _pinDerivedKey;
    private readonly byte[] _argonSalt;

    public DatabaseServiceTests()
    {
        var log = new Mock<ILoggingService>();
        _tempDir = new TempDirectory("RunFence_DbTest");
        _service = new DatabaseService(log.Object, configDir: _tempDir.Path, localDataDir: _tempDir.Path);
        _plaintextService = new DatabaseService(log.Object, allowPlaintextConfig: true, configDir: _tempDir.Path, localDataDir: _tempDir.Path);
        _pinDerivedKey = new byte[32];
        new Random(42).NextBytes(_pinDerivedKey);
        _argonSalt = new byte[32];
        new Random(99).NextBytes(_argonSalt);
    }

    public void Dispose()
    {
        _tempDir.Dispose();
    }

    [Fact]
    public void LoadConfig_NonExistentFile_ReturnsEmptyDatabase()
    {
        var db = _service.LoadConfig(_pinDerivedKey);
        Assert.NotNull(db);
        Assert.Empty(db.Apps);
    }

    [Fact]
    public void VerifyConfigIntegrity_NoFiles_ReturnsFirstRun()
    {
        var result = _service.VerifyConfigIntegrity(_pinDerivedKey);
        Assert.Equal(ConfigIntegrityResult.FirstRun, result);
    }

    [Fact]
    public void SaveConfig_And_LoadConfig_EncryptedRoundTrip()
    {
        var db = new AppDatabase();
        db.Apps.Add(new AppEntry { Name = "TestApp", ExePath = @"C:\test.exe" });

        _service.SaveConfig(db, _pinDerivedKey, _argonSalt);
        var loaded = _service.LoadConfig(_pinDerivedKey);

        Assert.Single(loaded.Apps);
        Assert.Equal("TestApp", loaded.Apps[0].Name);
    }

    [Fact]
    public void LoadConfig_PlaintextWithAllowPlaintextConfig_ParsesAsJson()
    {
        var json = """{"apps":[{"name":"TestApp","exePath":"C:\\test.exe"}],"credentials":[],"allowedIpcCallers":[],"settings":{}}""";
        File.WriteAllText(Path.Combine(_tempDir.Path, "config.dat"), json);

        var db = _plaintextService.LoadConfig(_pinDerivedKey);

        Assert.Single(db.Apps);
        Assert.Equal("TestApp", db.Apps[0].Name);
    }

    [Fact]
    public void LoadConfig_PlaintextWithoutAllowPlaintextConfig_ThrowsCryptographicException()
    {
        var json = """{"apps":[],"credentials":[],"allowedIpcCallers":[],"settings":{}}""";
        File.WriteAllText(Path.Combine(_tempDir.Path, "config.dat"), json);

        Assert.Throws<CryptographicException>(() => _service.LoadConfig(_pinDerivedKey));
    }

    [Fact]
    public void SaveConfig_ProducesRameOutput()
    {
        var db = new AppDatabase();
        _service.SaveConfig(db, _pinDerivedKey, _argonSalt);

        var raw = File.ReadAllBytes(Path.Combine(_tempDir.Path, "config.dat"));
        Assert.True(ConfigEncryptionHelper.HasEncryptionHeader(raw), "Expected RAME header");
    }

    [Fact]
    public void SaveConfig_And_VerifyIntegrity_RoundTrip()
    {
        var db = new AppDatabase();
        db.Apps.Add(new AppEntry { Name = "TestApp", ExePath = @"C:\test.exe" });

        _service.SaveConfig(db, _pinDerivedKey, _argonSalt);

        var result = _service.VerifyConfigIntegrity(_pinDerivedKey);
        Assert.Equal(ConfigIntegrityResult.Valid, result);
    }

    [Fact]
    public void VerifyConfigIntegrity_WrongKey_ReturnsDecryptionFailed()
    {
        var db = new AppDatabase();
        db.Apps.Add(new AppEntry { Name = "TestApp", ExePath = @"C:\test.exe" });

        _service.SaveConfig(db, _pinDerivedKey, _argonSalt);

        var wrongKey = new byte[32];
        new Random(77).NextBytes(wrongKey);

        var result = _service.VerifyConfigIntegrity(wrongKey);
        Assert.Equal(ConfigIntegrityResult.DecryptionFailed, result);
    }

    [Fact]
    public void VerifyConfigIntegrity_PlaintextFileWithoutRameHeader_ReturnsDecryptionFailed()
    {
        var json = """{"apps":[],"credentials":[],"allowedIpcCallers":[],"settings":{}}""";
        File.WriteAllText(Path.Combine(_tempDir.Path, "config.dat"), json);

        var result = _service.VerifyConfigIntegrity(_pinDerivedKey);
        Assert.Equal(ConfigIntegrityResult.DecryptionFailed, result);
    }

    [Fact]
    public void VerifyConfigIntegrity_EmptyFile_ReturnsFirstRun()
    {
        File.WriteAllBytes(Path.Combine(_tempDir.Path, "config.dat"), Array.Empty<byte>());

        var result = _service.VerifyConfigIntegrity(_pinDerivedKey);
        Assert.Equal(ConfigIntegrityResult.FirstRun, result);
    }

    [Fact]
    public void SaveCredentialStore_And_LoadCredentialStore_RoundTrip()
    {
        var store = new CredentialStore
        {
            ArgonSalt = new byte[32],
            EncryptedCanary = [1, 2, 3],
            Credentials = [new() { Sid = "S-1-5-21-0-0-0-1001" }]
        };

        _service.SaveCredentialStore(store);
        var loaded = _service.LoadCredentialStore();

        Assert.Single(loaded.Credentials);
        Assert.Equal("S-1-5-21-0-0-0-1001", loaded.Credentials[0].Sid);
    }

    [Fact]
    public void LoadCredentialStore_NoFile_ThrowsFileNotFound()
    {
        Assert.Throws<FileNotFoundException>(() => _service.LoadCredentialStore());
    }

    [Fact]
    public void LoadCredentialStore_InvalidSalt_ThrowsJsonException()
    {
        var store = new CredentialStore
        {
            ArgonSalt = new byte[16], // Wrong size, should be 32
            EncryptedCanary = [1, 2, 3]
        };

        _service.SaveCredentialStore(store);

        Assert.Throws<JsonException>(() => _service.LoadCredentialStore());
    }

    [Fact]
    public void LoadCredentialStore_EmptyCanary_ThrowsJsonException()
    {
        var store = new CredentialStore
        {
            ArgonSalt = new byte[Constants.Argon2SaltSize],
            EncryptedCanary = []
        };

        _service.SaveCredentialStore(store);

        Assert.Throws<JsonException>(() => _service.LoadCredentialStore());
    }

    [Fact]
    public void SaveCredentialStoreAndConfig_ProducesEncryptedConfig()
    {
        var store = new CredentialStore
        {
            ArgonSalt = new byte[Constants.Argon2SaltSize],
            EncryptedCanary = [1, 2, 3],
            Credentials = [new() { Sid = "S-1-5-21-0-0-0-1001" }]
        };
        var database = new AppDatabase();
        database.Apps.Add(new AppEntry { Name = "TestApp", ExePath = @"C:\test.exe" });

        _service.SaveCredentialStoreAndConfig(store, database, _pinDerivedKey);

        // Config should be decryptable with the correct key
        var configResult = _service.VerifyConfigIntegrity(_pinDerivedKey);
        Assert.Equal(ConfigIntegrityResult.Valid, configResult);

        // Credential store should round-trip
        var loaded = _service.LoadCredentialStore();
        Assert.Single(loaded.Credentials);
        Assert.Equal("S-1-5-21-0-0-0-1001", loaded.Credentials[0].Sid);
    }

    [Fact]
    public void SaveCredentialStoreAndConfig_WrongKey_ConfigIntegrityReturnsDecryptionFailed()
    {
        var store = new CredentialStore
        {
            ArgonSalt = new byte[Constants.Argon2SaltSize],
            EncryptedCanary = [1, 2, 3],
            Credentials = [new() { Sid = "S-1-5-21-0-0-0-1001" }]
        };
        var database = new AppDatabase();
        database.Apps.Add(new AppEntry { Name = "TestApp", ExePath = @"C:\test.exe" });

        _service.SaveCredentialStoreAndConfig(store, database, _pinDerivedKey);

        var wrongKey = new byte[32];
        new Random(77).NextBytes(wrongKey);

        var configResult = _service.VerifyConfigIntegrity(wrongKey);
        Assert.Equal(ConfigIntegrityResult.DecryptionFailed, configResult);
    }

    [Fact]
    public void SaveCredentialStoreAndConfig_EmptyDatabase_ConfigIntegrityStillValid()
    {
        var store = new CredentialStore
        {
            ArgonSalt = new byte[Constants.Argon2SaltSize],
            EncryptedCanary = [1, 2, 3]
        };
        var database = new AppDatabase(); // empty

        _service.SaveCredentialStoreAndConfig(store, database, _pinDerivedKey);

        var configResult = _service.VerifyConfigIntegrity(_pinDerivedKey);
        Assert.Equal(ConfigIntegrityResult.Valid, configResult);
    }

    [Fact]
    public void LoadConfig_JsonWithNullCollections_ReturnsNonNullCollections()
    {
        var json = """{"apps":null,"allowedIpcCallers":null,"settings":null}""";
        File.WriteAllText(Path.Combine(_tempDir.Path, "config.dat"), json);

        var db = _plaintextService.LoadConfig(_pinDerivedKey);

        Assert.NotNull(db.Apps);
        Assert.NotNull(db.Accounts);
        Assert.NotNull(db.Settings);
    }

    [Fact]
    public void LoadCredentialStore_JsonWithNullCredentials_ReturnsNonNullList()
    {
        var store = new CredentialStore
        {
            ArgonSalt = new byte[Constants.Argon2SaltSize],
            EncryptedCanary = [1, 2, 3],
            Credentials = null!
        };

        _service.SaveCredentialStore(store);
        var loaded = _service.LoadCredentialStore();

        Assert.NotNull(loaded.Credentials);
    }

    [Fact]
    public void SaveCredentialStoreAndConfig_WithAppFilter_ExcludesFilteredApps()
    {
        var log = new Mock<ILoggingService>();
        var mainApp = new AppEntry { Id = "main1", Name = "MainApp", ExePath = @"C:\main.exe" };
        var additionalApp = new AppEntry { Id = "add1", Name = "AddApp", ExePath = @"C:\add.exe" };

        // Filter that excludes additionalApp from main config
        var filter = new TestAppFilter(db =>
        {
            var filtered = new AppDatabase
            {
                Apps = db.Apps.Where(a => a.Id != "add1").ToList(),
                Accounts = db.Accounts.Where(a => !a.IsIpcCaller).ToList(),
                Settings = db.Settings
            };
            return filtered;
        });

        var dir = Path.Combine(_tempDir.Path, "filter_test");
        Directory.CreateDirectory(dir);
        var serviceWithFilter = new DatabaseService(log.Object, appFilter: filter, configDir: dir, localDataDir: dir);

        var database = new AppDatabase();
        database.Apps.Add(mainApp);
        database.Apps.Add(additionalApp);

        var store = new CredentialStore
        {
            ArgonSalt = new byte[Constants.Argon2SaltSize],
            EncryptedCanary = [1, 2, 3]
        };
        serviceWithFilter.SaveCredentialStoreAndConfig(store, database, _pinDerivedKey);

        // Loaded config should only have mainApp (filter excluded additionalApp)
        var loaded = serviceWithFilter.LoadConfig(_pinDerivedKey);
        Assert.Single(loaded.Apps);
        Assert.Equal("main1", loaded.Apps[0].Id);
    }

    [Fact]
    public void LoadAppConfig_And_SaveAppConfig_RoundTrip()
    {
        var configPath = Path.Combine(_tempDir.Path, "extra.dat");
        var config = new AppConfig();
        config.Apps.Add(new AppEntry { Id = "e1", Name = "ExtraApp", ExePath = @"C:\extra.exe" });

        _service.SaveAppConfig(config, configPath, _pinDerivedKey, _argonSalt);
        var loaded = _service.LoadAppConfig(configPath, _pinDerivedKey);

        Assert.Single(loaded.Apps);
        Assert.Equal("ExtraApp", loaded.Apps[0].Name);
    }

    [Fact]
    public void LoadAppConfig_WrongKey_ThrowsCryptographicException()
    {
        var configPath = Path.Combine(_tempDir.Path, "extra2.dat");
        var config = new AppConfig();
        _service.SaveAppConfig(config, configPath, _pinDerivedKey, _argonSalt);

        var wrongKey = new byte[32];
        new Random(99).NextBytes(wrongKey);

        Assert.ThrowsAny<CryptographicException>(() => _service.LoadAppConfig(configPath, wrongKey));
    }

    [Fact]
    public void SaveCredentialStoreAndAllConfigs_WritesAllFiles()
    {
        var store = new CredentialStore
        {
            ArgonSalt = new byte[Constants.Argon2SaltSize],
            EncryptedCanary = [1, 2, 3]
        };
        var database = new AppDatabase();
        database.Apps.Add(new AppEntry { Id = "m1", Name = "Main", ExePath = @"C:\main.exe" });

        var extraPath = Path.Combine(_tempDir.Path, "extra.dat");
        var extraConfig = new AppConfig();
        extraConfig.Apps.Add(new AppEntry { Id = "e1", Name = "Extra", ExePath = @"C:\extra.exe" });

        var additionalConfigs = new List<(string, AppConfig)> { (extraPath, extraConfig) };
        _service.SaveCredentialStoreAndAllConfigs(store, database, _pinDerivedKey, additionalConfigs);

        // Verify credential store
        var loadedStore = _service.LoadCredentialStore();
        Assert.NotNull(loadedStore);

        // Verify main config
        var loadedMain = _service.LoadConfig(_pinDerivedKey);
        Assert.Single(loadedMain.Apps);
        Assert.Equal("m1", loadedMain.Apps[0].Id);

        // Verify extra config
        var loadedExtra = _service.LoadAppConfig(extraPath, _pinDerivedKey);
        Assert.Single(loadedExtra.Apps);
        Assert.Equal("e1", loadedExtra.Apps[0].Id);
    }

    [Fact]
    public void SaveConfig_V2_EmbeddedSaltMatchesArgonSalt()
    {
        var db = new AppDatabase();
        _service.SaveConfig(db, _pinDerivedKey, _argonSalt);

        var extracted = _service.TryGetConfigSalt();

        Assert.NotNull(extracted);
        Assert.Equal(_argonSalt, extracted);
    }

    [Fact]
    public void TryGetConfigSalt_MissingFile_ReturnsNull()
    {
        // No config file written — should return null without throwing
        Assert.Null(_service.TryGetConfigSalt());
    }

    [Fact]
    public void TryGetConfigSalt_TruncatedV2File_ReturnsNull()
    {
        // 37 bytes: magic(4) + version 0x02(1) + fileType(1) + 31 salt bytes — one byte short of 38
        var truncated = new byte[37];
        truncated[0] = 0x52;
        truncated[1] = 0x41;
        truncated[2] = 0x4D;
        truncated[3] = 0x45; // RAME
        truncated[4] = 0x02; // v2
        truncated[5] = 0x01; // fileType
        // bytes 6-36 are zeroes (31 bytes)

        File.WriteAllBytes(Path.Combine(_tempDir.Path, "config.dat"), truncated);

        Assert.Null(_service.TryGetConfigSalt());
    }

    [Fact]
    public void SaveCredentialStoreAndConfig_V2_EmbeddedSaltFromStore()
    {
        var store = new CredentialStore
        {
            ArgonSalt = _argonSalt,
            EncryptedCanary = [1, 2, 3]
        };
        var database = new AppDatabase();

        _service.SaveCredentialStoreAndConfig(store, database, _pinDerivedKey);

        var extracted = _service.TryGetConfigSalt();
        Assert.NotNull(extracted);
        Assert.Equal(_argonSalt, extracted);
    }

    [Fact]
    public void SaveAppConfig_V2_EmbeddedSaltMatchesArgonSalt()
    {
        var configPath = Path.Combine(_tempDir.Path, "salt_test.dat");
        var config = new AppConfig();
        _service.SaveAppConfig(config, configPath, _pinDerivedKey, _argonSalt);

        var extracted = _service.TryGetAppConfigSalt(configPath);

        Assert.NotNull(extracted);
        Assert.Equal(_argonSalt, extracted);
    }

    [Fact]
    public void TryGetAppConfigSalt_MissingFile_ReturnsNull()
    {
        var missingPath = Path.Combine(_tempDir.Path, "nonexistent.dat");
        Assert.Null(_service.TryGetAppConfigSalt(missingPath));
    }

    [Fact]
    public void SaveCredentialStoreAndConfig_WhenConfigWriteFails_RollsBackCredentials()
    {
        // Arrange: write an initial credential store so there's a pre-existing file to restore
        var initialStore = new CredentialStore
        {
            ArgonSalt = new byte[Constants.Argon2SaltSize],
            EncryptedCanary = [0xAA, 0xBB, 0xCC] // sentinel value
        };
        _service.SaveCredentialStore(initialStore);

        var initialCredentialsBytes = File.ReadAllBytes(
            Path.Combine(_tempDir.Path, "credentials.dat"));

        // Create a directory where config.dat should be written — this will cause Phase 2 to fail
        // when AtomicWriteBatch tries to File.Replace/Move the .tmp file to config.dat
        var configPath = Path.Combine(_tempDir.Path, "config.dat");
        Directory.CreateDirectory(configPath); // now config.dat is a directory, not a file

        var newStore = new CredentialStore
        {
            ArgonSalt = new byte[Constants.Argon2SaltSize],
            EncryptedCanary = [0x11, 0x22, 0x33] // different from initial
        };
        var database = new AppDatabase();

        // Act & Assert: the write batch must throw because config.dat is a directory
        Assert.ThrowsAny<Exception>(() =>
            _service.SaveCredentialStoreAndConfig(newStore, database, _pinDerivedKey));

        // Rollback: credentials.dat must be restored to the initial content
        var afterBytes = File.ReadAllBytes(Path.Combine(_tempDir.Path, "credentials.dat"));
        Assert.Equal(initialCredentialsBytes, afterBytes);
    }

    // Helper for filter test
    private class TestAppFilter(Func<AppDatabase, AppDatabase> filterFn) : IAppFilter
    {
        public AppDatabase FilterForMainConfig(AppDatabase database) => filterFn(database);
    }
}