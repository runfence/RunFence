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
    private readonly Mock<ILoggingService> _log = new();
    private readonly PersistenceAtomicFileWriter _atomicFileWriter = new(new PersistenceFileSecurityMirror());
    private readonly DatabaseService _service;
    private readonly DatabaseService _plaintextService;
    private readonly TempDirectory _tempDir;
    private readonly byte[] _pinDerivedKey;
    private readonly SecureSecret _pinDerivedKeySource;
    private readonly byte[] _argonSalt;

    public DatabaseServiceTests()
    {
        _tempDir = new TempDirectory("RunFence_DbTest");
        var paths = new TestConfigPaths(_tempDir.Path);
        _service = new DatabaseService(_log.Object, paths, _atomicFileWriter, appFilter: null, allowPlaintextConfig: false);
        _plaintextService = new DatabaseService(_log.Object, paths, _atomicFileWriter, appFilter: null, allowPlaintextConfig: true);
        _pinDerivedKey = new byte[32];
        new Random(42).NextBytes(_pinDerivedKey);
        _pinDerivedKeySource = TestSecretFactory.FromBytes(_pinDerivedKey);
        _argonSalt = new byte[32];
        new Random(99).NextBytes(_argonSalt);
    }

    public void Dispose()
    {
        _pinDerivedKeySource.Dispose();
        _tempDir.Dispose();
    }

    [Fact]
    public void LoadConfig_NonExistentFile_ReturnsEmptyDatabase()
    {
        var db = _service.LoadConfig(_pinDerivedKeySource);
        Assert.NotNull(db);
        Assert.Empty(db.Apps);
    }

    [Theory]
    [InlineData(true)]  // file exists, loaded from disk
    [InlineData(false)] // file missing, returns empty database
    public void LoadConfig_SystemAccount_HasHighestAllowed(bool fileExists)
    {
        // Arrange
        if (fileExists)
            _service.SaveConfig(new AppDatabase(), _pinDerivedKeySource, _argonSalt);

        // Act
        var db = _service.LoadConfig(_pinDerivedKeySource);

        // Assert: SYSTEM account is present with HighestAllowed regardless of whether the file existed
        var system = db.GetAccount(SidConstants.SystemSid);
        Assert.NotNull(system);
        Assert.Equal(PrivilegeLevel.HighestAllowed, system.PrivilegeLevel);
    }

    [Fact]
    public void VerifyConfigIntegrity_NoFiles_ReturnsFirstRun()
    {
        var result = _service.VerifyConfigIntegrity(_pinDerivedKeySource);
        Assert.Equal(ConfigIntegrityResult.FirstRun, result);
    }

    [Fact]
    public void SaveConfig_And_LoadConfig_EncryptedRoundTrip()
    {
        var db = new AppDatabase();
        db.Apps.Add(new AppEntry { Name = "TestApp", ExePath = @"C:\test.exe" });

        _service.SaveConfig(db, _pinDerivedKeySource, _argonSalt);
        var loaded = _service.LoadConfig(_pinDerivedKeySource);

        Assert.Single(loaded.Apps);
        Assert.Equal("TestApp", loaded.Apps[0].Name);
    }

    [Fact]
    public void SaveConfig_And_LoadConfig_SnapshotSourceRoundTrip()
    {
        var db = new AppDatabase();
        db.Apps.Add(new AppEntry { Name = "SnapshotApp", ExePath = @"C:\snapshot.exe" });
        var pinKey = CreateSnapshotSource();

        _service.SaveConfig(db, pinKey, _argonSalt);
        var loaded = _service.LoadConfig(pinKey);

        Assert.Single(loaded.Apps);
        Assert.Equal("SnapshotApp", loaded.Apps[0].Name);
    }

    [Fact]
    public void LoadConfig_PlaintextWithAllowPlaintextConfig_ParsesAsJson()
    {
        var json = """{"apps":[{"name":"TestApp","exePath":"C:\\test.exe"}],"credentials":[],"allowedIpcCallers":[],"settings":{}}""";
        File.WriteAllText(Path.Combine(_tempDir.Path, "config.dat"), json);

        var db = _plaintextService.LoadConfig(_pinDerivedKeySource);

        Assert.Single(db.Apps);
        Assert.Equal("TestApp", db.Apps[0].Name);
    }

    [Fact]
    public void LoadConfig_PlaintextWithoutAllowPlaintextConfig_ThrowsCryptographicException()
    {
        var json = """{"apps":[],"credentials":[],"allowedIpcCallers":[],"settings":{}}""";
        File.WriteAllText(Path.Combine(_tempDir.Path, "config.dat"), json);

        Assert.Throws<CryptographicException>(() => _service.LoadConfig(_pinDerivedKeySource));
    }

    [Fact]
    public void SaveConfig_ProducesRameOutput()
    {
        var db = new AppDatabase();
        _service.SaveConfig(db, _pinDerivedKeySource, _argonSalt);

        var raw = File.ReadAllBytes(Path.Combine(_tempDir.Path, "config.dat"));
        Assert.True(ConfigEncryptionHelper.HasEncryptionHeader(raw), "Expected RAME header");
    }

    [Fact]
    public void SaveConfig_And_VerifyIntegrity_RoundTrip()
    {
        var db = new AppDatabase();
        db.Apps.Add(new AppEntry { Name = "TestApp", ExePath = @"C:\test.exe" });

        _service.SaveConfig(db, _pinDerivedKeySource, _argonSalt);

        var result = _service.VerifyConfigIntegrity(_pinDerivedKeySource);
        Assert.Equal(ConfigIntegrityResult.Valid, result);
    }

    [Fact]
    public void VerifyConfigIntegrity_WrongKey_ReturnsDecryptionFailed()
    {
        var db = new AppDatabase();
        db.Apps.Add(new AppEntry { Name = "TestApp", ExePath = @"C:\test.exe" });

        _service.SaveConfig(db, _pinDerivedKeySource, _argonSalt);

        var wrongKey = new byte[32];
        new Random(77).NextBytes(wrongKey);

        using var wrongKeySource = TestSecretFactory.FromBytes(wrongKey);
        var result = _service.VerifyConfigIntegrity(wrongKeySource);
        Assert.Equal(ConfigIntegrityResult.DecryptionFailed, result);
    }

    [Fact]
    public void VerifyConfigIntegrity_PlaintextFileWithoutRameHeader_ReturnsDecryptionFailed()
    {
        var json = """{"apps":[],"credentials":[],"allowedIpcCallers":[],"settings":{}}""";
        File.WriteAllText(Path.Combine(_tempDir.Path, "config.dat"), json);

        var result = _service.VerifyConfigIntegrity(_pinDerivedKeySource);
        Assert.Equal(ConfigIntegrityResult.DecryptionFailed, result);
    }

    [Fact]
    public void VerifyConfigIntegrity_EmptyFile_ReturnsFirstRun()
    {
        File.WriteAllBytes(Path.Combine(_tempDir.Path, "config.dat"), Array.Empty<byte>());

        var result = _service.VerifyConfigIntegrity(_pinDerivedKeySource);
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
    public void SaveCredentialStore_And_LoadCredentialStore_PreservesAllFields()
    {
        // Direct round-trip: all CredentialStore fields survive save/load intact
        var expectedSalt = new byte[Constants.Argon2SaltSize];
        new Random(55).NextBytes(expectedSalt);
        var expectedCanary = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD };
        var store = new CredentialStore
        {
            ArgonSalt = expectedSalt,
            EncryptedCanary = expectedCanary,
            Credentials = [new() { Sid = "S-1-5-21-0-0-0-1001", Id = Guid.NewGuid() }]
        };

        _service.SaveCredentialStore(store);
        var loaded = _service.LoadCredentialStore();

        Assert.Equal(expectedSalt, loaded.ArgonSalt);
        Assert.Equal(expectedCanary, loaded.EncryptedCanary);
        Assert.Single(loaded.Credentials);
        Assert.Equal("S-1-5-21-0-0-0-1001", loaded.Credentials[0].Sid);
        Assert.Equal(store.Credentials[0].Id, loaded.Credentials[0].Id);
    }

    [Fact]
    public void SaveConfig_And_LoadConfig_PreservesAllAppFields()
    {
        // Direct round-trip: AppEntry fields survive encrypted save/load intact
        var app = new AppEntry
        {
            Id = "roundtrip-id",
            Name = "RoundtripApp",
            ExePath = @"C:\apps\roundtrip.exe",
            RestrictAcl = true,
            ManageShortcuts = true,
            IsUrlScheme = false
        };
        var db = new AppDatabase();
        db.Apps.Add(app);

        _service.SaveConfig(db, _pinDerivedKeySource, _argonSalt);
        var loaded = _service.LoadConfig(_pinDerivedKeySource);

        Assert.Single(loaded.Apps);
        var loadedApp = loaded.Apps[0];
        Assert.Equal("roundtrip-id", loadedApp.Id);
        Assert.Equal("RoundtripApp", loadedApp.Name);
        Assert.Equal(@"C:\apps\roundtrip.exe", loadedApp.ExePath);
        Assert.True(loadedApp.RestrictAcl);
        Assert.True(loadedApp.ManageShortcuts);
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

        _service.SaveCredentialStoreAndConfig(store, database, _pinDerivedKeySource);

        // Config should be decryptable with the correct key
        var configResult = _service.VerifyConfigIntegrity(_pinDerivedKeySource);
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

        _service.SaveCredentialStoreAndConfig(store, database, _pinDerivedKeySource);

        var wrongKey = new byte[32];
        new Random(77).NextBytes(wrongKey);

        using var wrongKeySource = TestSecretFactory.FromBytes(wrongKey);
        var configResult = _service.VerifyConfigIntegrity(wrongKeySource);
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

        _service.SaveCredentialStoreAndConfig(store, database, _pinDerivedKeySource);

        var configResult = _service.VerifyConfigIntegrity(_pinDerivedKeySource);
        Assert.Equal(ConfigIntegrityResult.Valid, configResult);
    }

    [Fact]
    public void LoadConfig_JsonWithNullCollections_ReturnsNonNullCollections()
    {
        var json = """{"apps":null,"accounts":null,"appContainers":null,"allowedIpcCallers":null,"settings":null,"sidNames":null}""";
        File.WriteAllText(Path.Combine(_tempDir.Path, "config.dat"), json);

        var db = _plaintextService.LoadConfig(_pinDerivedKeySource);

        Assert.NotNull(db.Apps);
        Assert.NotNull(db.Accounts);
        Assert.NotNull(db.AppContainers);
        Assert.NotNull(db.Settings);
        Assert.NotNull(db.SidNames);
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
        var serviceWithFilter = new DatabaseService(
            log.Object,
            new TestConfigPaths(dir),
            _atomicFileWriter,
            appFilter: filter,
            allowPlaintextConfig: false);

        var database = new AppDatabase();
        database.Apps.Add(mainApp);
        database.Apps.Add(additionalApp);

        var store = new CredentialStore
        {
            ArgonSalt = new byte[Constants.Argon2SaltSize],
            EncryptedCanary = [1, 2, 3]
        };
        serviceWithFilter.SaveCredentialStoreAndConfig(store, database, _pinDerivedKeySource);

        // Loaded config should only have mainApp (filter excluded additionalApp)
        var loaded = serviceWithFilter.LoadConfig(_pinDerivedKeySource);
        Assert.Single(loaded.Apps);
        Assert.Equal("main1", loaded.Apps[0].Id);
    }

    [Fact]
    public void LoadAppConfig_And_SaveAppConfig_RoundTrip()
    {
        var configPath = Path.Combine(_tempDir.Path, "extra.dat");
        var config = new AppConfig();
        config.Apps.Add(new AppEntry { Id = "e1", Name = "ExtraApp", ExePath = @"C:\extra.exe" });

        _service.SaveAppConfig(config, configPath, _pinDerivedKeySource, _argonSalt);
        var loaded = _service.LoadAppConfigFromPath(configPath, _pinDerivedKeySource);

        Assert.Single(loaded.Apps);
        Assert.Equal("ExtraApp", loaded.Apps[0].Name);
    }

    [Fact]
    public void LoadAppConfig_And_SaveAppConfig_SnapshotSourceRoundTrip()
    {
        var configPath = Path.Combine(_tempDir.Path, "extra-snapshot.dat");
        var config = new AppConfig();
        config.Apps.Add(new AppEntry { Id = "e2", Name = "ExtraSnapshot", ExePath = @"C:\extra-snapshot.exe" });
        var pinKey = CreateSnapshotSource();

        _service.SaveAppConfig(config, configPath, pinKey, _argonSalt);
        var loaded = _service.LoadAppConfigFromPath(configPath, pinKey);

        Assert.Single(loaded.Apps);
        Assert.Equal("ExtraSnapshot", loaded.Apps[0].Name);
    }

    [Fact]
    public void LoadAppConfig_WrongKey_ThrowsCryptographicException()
    {
        var configPath = Path.Combine(_tempDir.Path, "extra2.dat");
        var config = new AppConfig();
        _service.SaveAppConfig(config, configPath, _pinDerivedKeySource, _argonSalt);

        var wrongKey = new byte[32];
        new Random(99).NextBytes(wrongKey);

        using var wrongKeySource = TestSecretFactory.FromBytes(wrongKey);
        Assert.ThrowsAny<CryptographicException>(() => _service.LoadAppConfigFromPath(configPath, wrongKeySource));
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
        _service.SaveCredentialStoreAndAllConfigs(store, database, _pinDerivedKeySource, additionalConfigs);

        // Verify credential store
        var loadedStore = _service.LoadCredentialStore();
        Assert.NotNull(loadedStore);

        // Verify main config
        var loadedMain = _service.LoadConfig(_pinDerivedKeySource);
        Assert.Single(loadedMain.Apps);
        Assert.Equal("m1", loadedMain.Apps[0].Id);

        // Verify extra config
        var loadedExtra = _service.LoadAppConfigFromPath(extraPath, _pinDerivedKeySource);
        Assert.Single(loadedExtra.Apps);
        Assert.Equal("e1", loadedExtra.Apps[0].Id);
    }

    [Fact]
    public void SaveCredentialStoreAndAllConfigs_SnapshotSource_WritesAllFiles()
    {
        var store = new CredentialStore
        {
            ArgonSalt = new byte[Constants.Argon2SaltSize],
            EncryptedCanary = [1, 2, 3]
        };
        var database = new AppDatabase();
        database.Apps.Add(new AppEntry { Id = "m2", Name = "MainSnapshot", ExePath = @"C:\main-snapshot.exe" });

        var extraPath = Path.Combine(_tempDir.Path, "extra-snapshot-all.dat");
        var extraConfig = new AppConfig();
        extraConfig.Apps.Add(new AppEntry { Id = "e2", Name = "ExtraSnapshot", ExePath = @"C:\extra-snapshot.exe" });
        var pinKey = CreateSnapshotSource();

        _service.SaveCredentialStoreAndAllConfigs(store, database, pinKey, [(extraPath, extraConfig)]);

        var loadedMain = _service.LoadConfig(pinKey);
        var loadedExtra = _service.LoadAppConfigFromPath(extraPath, pinKey);

        Assert.Single(loadedMain.Apps);
        Assert.Equal("m2", loadedMain.Apps[0].Id);
        Assert.Single(loadedExtra.Apps);
        Assert.Equal("e2", loadedExtra.Apps[0].Id);
    }

    [Fact]
    public void SaveCredentialStoreAndConfig_WhenSecondBatchReplaceFails_RestoresPreviousFilesAndCleansArtifacts()
    {
        var storeBefore = CreateCredentialStore("before-cred");
        var storeAfter = CreateCredentialStore("after-cred");
        var databaseBefore = CreateDatabaseWithApp("before-main");
        var databaseAfter = CreateDatabaseWithApp("after-main");
        _service.SaveCredentialStoreAndConfig(storeBefore, databaseBefore, _pinDerivedKeySource);

        var credentialsPath = Path.Combine(_tempDir.Path, "credentials.dat");
        var configPath = Path.Combine(_tempDir.Path, "config.dat");
        var originalCredentialBytes = File.ReadAllBytes(credentialsPath);
        var originalConfigBytes = File.ReadAllBytes(configPath);

        using var configLock = new FileStream(configPath, FileMode.Open, FileAccess.Read, FileShare.Read);

        Assert.ThrowsAny<IOException>(() =>
            _service.SaveCredentialStoreAndConfig(storeAfter, databaseAfter, _pinDerivedKeySource));

        Assert.Equal(originalCredentialBytes, File.ReadAllBytes(credentialsPath));
        Assert.Equal(originalConfigBytes, File.ReadAllBytes(configPath));
        AssertNoManagedPersistenceArtifacts(_tempDir.Path);
    }

    [Fact]
    public void SaveCredentialStoreAndAllConfigs_WhenLaterBatchReplaceFails_RestoresPreviousFilesAndCleansArtifacts()
    {
        var storeBefore = CreateCredentialStore("before-cred");
        var storeAfter = CreateCredentialStore("after-cred");
        var databaseBefore = CreateDatabaseWithApp("before-main");
        var databaseAfter = CreateDatabaseWithApp("after-main");
        var additionalOnePath = Path.Combine(_tempDir.Path, "extra-one.dat");
        var additionalTwoPath = Path.Combine(_tempDir.Path, "extra-two.dat");
        var additionalBefore = new List<(string path, AppConfig config)>
        {
            (additionalOnePath, CreateAdditionalConfig("before-extra-one")),
            (additionalTwoPath, CreateAdditionalConfig("before-extra-two"))
        };
        var additionalAfter = new List<(string path, AppConfig config)>
        {
            (additionalOnePath, CreateAdditionalConfig("after-extra-one")),
            (additionalTwoPath, CreateAdditionalConfig("after-extra-two"))
        };
        _service.SaveCredentialStoreAndAllConfigs(storeBefore, databaseBefore, _pinDerivedKeySource, additionalBefore);

        var credentialsPath = Path.Combine(_tempDir.Path, "credentials.dat");
        var configPath = Path.Combine(_tempDir.Path, "config.dat");
        var originalCredentialBytes = File.ReadAllBytes(credentialsPath);
        var originalConfigBytes = File.ReadAllBytes(configPath);
        var originalAdditionalOneBytes = File.ReadAllBytes(additionalOnePath);
        var originalAdditionalTwoBytes = File.ReadAllBytes(additionalTwoPath);

        using var additionalTwoLock = new FileStream(additionalTwoPath, FileMode.Open, FileAccess.Read, FileShare.Read);

        Assert.ThrowsAny<IOException>(() =>
            _service.SaveCredentialStoreAndAllConfigs(storeAfter, databaseAfter, _pinDerivedKeySource, additionalAfter));

        Assert.Equal(originalCredentialBytes, File.ReadAllBytes(credentialsPath));
        Assert.Equal(originalConfigBytes, File.ReadAllBytes(configPath));
        Assert.Equal(originalAdditionalOneBytes, File.ReadAllBytes(additionalOnePath));
        Assert.Equal(originalAdditionalTwoBytes, File.ReadAllBytes(additionalTwoPath));
        AssertNoManagedPersistenceArtifacts(_tempDir.Path);
    }

    [Fact]
    public void SaveCredentialStoreAndConfig_ComposesExpectedBatchPathsOnce()
    {
        var recordingWriter = new RecordingAtomicFileWriter();
        var service = CreateService(recordingWriter, Path.Combine(_tempDir.Path, "record-save-both"));
        var store = CreateCredentialStore("record-cred");
        var database = CreateDatabaseWithApp("record-main");

        service.SaveCredentialStoreAndConfig(store, database, _pinDerivedKeySource);

        var batch = Assert.Single(recordingWriter.Batches);
        var expectedPaths = new[]
        {
            Path.Combine(Path.Combine(_tempDir.Path, "record-save-both"), "credentials.dat"),
            Path.Combine(Path.Combine(_tempDir.Path, "record-save-both"), "config.dat")
        };
        Assert.Equal(expectedPaths, batch.Select(entry => entry.path).ToArray());
        Assert.Equal(expectedPaths.Length, batch.Select(entry => entry.path).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void SaveCredentialStoreAndAllConfigs_ComposesExpectedBatchPathsOnce()
    {
        var recordingWriter = new RecordingAtomicFileWriter();
        var dir = Path.Combine(_tempDir.Path, "record-save-all");
        var service = CreateService(recordingWriter, dir);
        var store = CreateCredentialStore("record-cred");
        var database = CreateDatabaseWithApp("record-main");
        var extraOnePath = Path.Combine(dir, "extra-one.dat");
        var extraTwoPath = Path.Combine(dir, "extra-two.dat");

        service.SaveCredentialStoreAndAllConfigs(
            store,
            database,
            _pinDerivedKeySource,
            [
                (extraOnePath, CreateAdditionalConfig("record-extra-one")),
                (extraTwoPath, CreateAdditionalConfig("record-extra-two"))
            ]);

        var batch = Assert.Single(recordingWriter.Batches);
        var expectedPaths = new[]
        {
            Path.Combine(dir, "credentials.dat"),
            Path.Combine(dir, "config.dat"),
            extraOnePath,
            extraTwoPath
        };
        Assert.Equal(expectedPaths, batch.Select(entry => entry.path).ToArray());
        Assert.Equal(expectedPaths.Length, batch.Select(entry => entry.path).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void SaveConfig_V2_EmbeddedSaltMatchesArgonSalt()
    {
        var db = new AppDatabase();
        _service.SaveConfig(db, _pinDerivedKeySource, _argonSalt);

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

        _service.SaveCredentialStoreAndConfig(store, database, _pinDerivedKeySource);

        var extracted = _service.TryGetConfigSalt();
        Assert.NotNull(extracted);
        Assert.Equal(_argonSalt, extracted);
    }

    [Fact]
    public void SaveAppConfig_V2_EmbeddedSaltMatchesArgonSalt()
    {
        var configPath = Path.Combine(_tempDir.Path, "salt_test.dat");
        var config = new AppConfig();
        _service.SaveAppConfig(config, configPath, _pinDerivedKeySource, _argonSalt);

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
    public void LoadConfig_SuccessfulEncryptedLoad_DoesNotPreserveLoadedGoodBackup()
    {
        var configPath = Path.Combine(_tempDir.Path, "config.dat");
        var first = new AppDatabase();
        first.Apps.Add(new AppEntry { Id = "first", Name = "First", ExePath = @"C:\first.exe" });
        _service.SaveConfig(first, _pinDerivedKeySource, _argonSalt);

        _service.LoadConfig(_pinDerivedKeySource);

        Assert.False(File.Exists(configPath + ".lastgood"));
    }

    [Fact]
    public void LoadConfigFromPath_DoesNotPreserveLoadedGoodBackup()
    {
        var backupPath = Path.Combine(_tempDir.Path, "config.dat.lastgood");
        var db = new AppDatabase();
        db.Apps.Add(new AppEntry { Id = "backup", Name = "Backup", ExePath = @"C:\backup.exe" });
        _service.SaveConfig(db, _pinDerivedKeySource, _argonSalt);
        File.Copy(Path.Combine(_tempDir.Path, "config.dat"), backupPath, overwrite: true);

        var loaded = _service.LoadConfigFromPath(backupPath, _pinDerivedKeySource);

        Assert.Single(loaded.Apps);
        Assert.Equal("backup", loaded.Apps[0].Id);
        Assert.False(File.Exists(backupPath + ".lastgood"));
    }

    [Fact]
    public void SaveConfig_DoesNotPreserveLoadedGoodBackup()
    {
        var second = new AppDatabase();
        second.Apps.Add(new AppEntry { Id = "second", Name = "Second", ExePath = @"C:\second.exe" });
        _service.SaveConfig(second, _pinDerivedKeySource, _argonSalt);

        Assert.False(File.Exists(Path.Combine(_tempDir.Path, "config.dat.lastgood")));
    }

    [Fact]
    public void LoadAppConfig_SuccessfulEncryptedLoad_DoesNotPreserveLoadedGoodBackup()
    {
        var configPath = Path.Combine(_tempDir.Path, "extra-lastgood.dat");
        var config = new AppConfig();
        config.Apps.Add(new AppEntry { Id = "extra", Name = "Extra", ExePath = @"C:\extra.exe" });
        _service.SaveAppConfig(config, configPath, _pinDerivedKeySource, _argonSalt);

        _service.LoadAppConfigFromPath(configPath, _pinDerivedKeySource);

        Assert.False(File.Exists(configPath + ".lastgood"));
    }

    [Fact]
    public void LoadCredentialStore_DoesNotPreserveLoadedGoodBackupDuringRawLoad()
    {
        var store = new CredentialStore
        {
            ArgonSalt = new byte[Constants.Argon2SaltSize],
            EncryptedCanary = [1, 2, 3]
        };
        _service.SaveCredentialStore(store);

        _service.LoadCredentialStore();

        Assert.False(File.Exists(Path.Combine(_tempDir.Path, "credentials.dat.lastgood")));
    }

    [Fact]
    public void LoadCredentialStoreFromPath_ReturnsStoreWithoutPreservingLoadedGoodBackup()
    {
        var credentialsPath = Path.Combine(_tempDir.Path, "credentials.dat");
        var backupPath = credentialsPath + ".lastgood";
        var store = new CredentialStore
        {
            ArgonSalt = new byte[Constants.Argon2SaltSize],
            EncryptedCanary = [1, 2, 3]
        };
        File.WriteAllText(backupPath, JsonSerializer.Serialize(store, JsonDefaults.Options));

        var result = _service.LoadCredentialStoreFromPath(backupPath);

        Assert.Equal(store.ArgonSalt, result.ArgonSalt);
        Assert.False(File.Exists(backupPath + ".lastgood"));
    }

    [Fact]
    public void LoadCredentialStore_DoesNotLogLoadedGoodBackupWarning()
    {
        var store = new CredentialStore
        {
            ArgonSalt = new byte[Constants.Argon2SaltSize],
            EncryptedCanary = [1, 2, 3]
        };
        _service.SaveCredentialStore(store);

        var loaded = _service.LoadCredentialStore();

        Assert.Equal(store.ArgonSalt, loaded.ArgonSalt);
        _log.Verify(l => l.Warn(It.IsAny<string>()), Times.Never);
    }

    // Helper for filter test
    private class TestAppFilter(Func<AppDatabase, AppDatabase> filterFn) : IAppFilter
    {
        public AppDatabase FilterForMainConfig(AppDatabase database) => filterFn(database);
    }

    private ISecureSecretSnapshotSource CreateSnapshotSource() => _pinDerivedKeySource;

    private DatabaseService CreateService(IPersistenceAtomicFileWriter atomicFileWriter, string dir)
        => new(_log.Object, new TestConfigPaths(dir), atomicFileWriter, appFilter: null, allowPlaintextConfig: false);

    private static CredentialStore CreateCredentialStore(string sidSuffix) => new()
    {
        ArgonSalt = Enumerable.Repeat((byte)sidSuffix.Length, Constants.Argon2SaltSize).ToArray(),
        EncryptedCanary = [1, 2, 3, 4],
        Credentials =
        [
            new CredentialEntry
            {
                Sid = $"S-1-5-21-0-0-0-{Math.Abs(sidSuffix.GetHashCode())}",
                Id = Guid.NewGuid()
            }
        ]
    };

    private static AppDatabase CreateDatabaseWithApp(string appId)
    {
        var database = new AppDatabase();
        database.Apps.Add(new AppEntry
        {
            Id = appId,
            Name = appId,
            ExePath = $@"C:\{appId}.exe"
        });
        return database;
    }

    private static AppConfig CreateAdditionalConfig(string appId)
    {
        var config = new AppConfig();
        config.Apps.Add(new AppEntry
        {
            Id = appId,
            Name = appId,
            ExePath = $@"C:\{appId}.exe"
        });
        return config;
    }

    private static void AssertNoManagedPersistenceArtifacts(string dir)
    {
        Assert.Empty(Directory.GetFiles(dir, "*.tmp", SearchOption.TopDirectoryOnly));
        Assert.Empty(Directory.GetFiles(dir, "*.rollback", SearchOption.TopDirectoryOnly));
    }

    private class TestConfigPaths(string dir) : IConfigPaths
    {
        public string ConfigFilePath => Path.Combine(dir, "config.dat");
        public string CredentialsFilePath => Path.Combine(dir, "credentials.dat");
        public string LicenseFilePath => Path.Combine(dir, "license.dat");
        public string RememberPinFilePath => Path.Combine(dir, "startkey.dat");
        public string LocalDataDir => dir;
    }

    private sealed class RecordingAtomicFileWriter : IPersistenceAtomicFileWriter
    {
        public List<IReadOnlyList<(string path, byte[] data)>> Batches { get; } = [];

        public void AtomicWrite(string targetPath, byte[] data)
            => throw new NotSupportedException("This test expects batch writes only.");

        public void AtomicWrite(string targetPath, byte[] data, System.Security.AccessControl.FileSecurity? finalSecurity)
            => throw new NotSupportedException("This test expects batch writes only.");

        public void AtomicCopy(string sourcePath, string targetPath, System.Security.AccessControl.FileSecurity? finalSecurity)
            => throw new NotSupportedException("This test does not expect AtomicCopy.");

        public void AtomicWriteBatch(IReadOnlyList<(string path, byte[] data)> files)
            => Batches.Add(files.Select(file => (file.path, file.data.ToArray())).ToArray());
    }
}
