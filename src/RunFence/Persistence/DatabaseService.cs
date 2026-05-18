using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RunFence.Core;
using RunFence.Core.Models;
// ReSharper disable NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract

namespace RunFence.Persistence;

public class DatabaseService(
    ILoggingService log,
    IConfigPaths configPaths,
    IPersistenceAtomicFileWriter atomicFileWriter,
    IAppFilter? appFilter,
    bool allowPlaintextConfig)
    : IDatabaseService
{
    private readonly Lock _saveLock = new();

    private string ConfigFilePath => configPaths.ConfigFilePath;
    private string CredentialsFilePath => configPaths.CredentialsFilePath;

    // --- IConfigRepository ---

    public AppDatabase LoadConfig(ISecureSecretSnapshotSource pinDerivedKey)
        => LoadConfigCore(
            ConfigFilePath,
            raw => pinDerivedKey.TransformSnapshot(
                key => ConfigEncryptionHelper.DecryptConfig(raw, key, ConfigFileType.MainConfig)),
            allowMissingAsFirstRun: true);

    public AppDatabase LoadConfigFromPath(string configPath, ISecureSecretSnapshotSource pinDerivedKey)
        => LoadConfigCore(
            configPath,
            raw => pinDerivedKey.TransformSnapshot(
                key => ConfigEncryptionHelper.DecryptConfig(raw, key, ConfigFileType.MainConfig)),
            allowMissingAsFirstRun: false);

    private AppDatabase LoadConfigCore(
        string path,
        Func<byte[], byte[]> decryptConfig,
        bool allowMissingAsFirstRun)
    {
        AppDatabase db;
        if (!File.Exists(path))
        {
            if (!allowMissingAsFirstRun)
                throw new FileNotFoundException("Config file not found.", path);

            log.Info("Config file not found, returning empty database.");
            db = new AppDatabase();
        }
        else
        {
            var raw = File.ReadAllBytes(path);

            byte[]? json = null;
            var isEncrypted = ConfigEncryptionHelper.HasEncryptionHeader(raw);
            if (!isEncrypted && !allowPlaintextConfig)
            {
                throw new CryptographicException("Config file is not encrypted.");
            }

            try
            {
                json = isEncrypted ? decryptConfig(raw) : raw;
                db = JsonSerializer.Deserialize<AppDatabase>(json, JsonDefaults.Options) ?? new AppDatabase();
                db.Apps ??= [];
                db.Accounts ??= [];
                db.AppContainers ??= [];
                db.Settings ??= new();
                db.SidNames ??= new(StringComparer.OrdinalIgnoreCase);
            }
            finally
            {
                if (isEncrypted && json != null)
                    CryptographicOperations.ZeroMemory(json);
            }
        }

        WellKnownAccountDefaults.Apply(db);
        return db;
    }

    public void SaveConfig(AppDatabase database, ISecureSecretSnapshotSource pinDerivedKey, byte[] argonSalt)
        => SaveConfigCore(database, argonSalt, json => pinDerivedKey.TransformSnapshot(
            key => ConfigEncryptionHelper.EncryptConfig(json, key, ConfigFileType.MainConfig, argonSalt)));

    private void SaveConfigCore(AppDatabase database, byte[] argonSalt, Func<byte[], byte[]> encryptConfig)
    {
        lock (_saveLock)
        {
            var dbToSave = ApplyFilter(database);
            var encrypted = SerializeAndEncrypt(dbToSave, encryptConfig);
            AtomicWrite(ConfigFilePath, encrypted);
            log.Info("Config saved (encrypted).");
        }
    }

    public ConfigIntegrityResult VerifyConfigIntegrity(ISecureSecretSnapshotSource pinDerivedKey)
        => VerifyConfigIntegrityCore(raw => pinDerivedKey.TransformSnapshot(
            key => ConfigEncryptionHelper.DecryptConfig(raw, key, ConfigFileType.MainConfig)));

    private ConfigIntegrityResult VerifyConfigIntegrityCore(Func<byte[], byte[]> decryptConfig)
    {
        var configPath = ConfigFilePath;

        if (!File.Exists(configPath))
            return ConfigIntegrityResult.FirstRun;

        var raw = File.ReadAllBytes(configPath);
        if (raw.Length == 0)
            return ConfigIntegrityResult.FirstRun;

        if (!ConfigEncryptionHelper.HasEncryptionHeader(raw))
            return ConfigIntegrityResult.DecryptionFailed;

        try
        {
            byte[]? json = null;
            try
            {
                json = decryptConfig(raw);
            }
            finally
            {
                if (json != null)
                    CryptographicOperations.ZeroMemory(json);
            }

            return ConfigIntegrityResult.Valid;
        }
        catch (CryptographicException ex)
        {
            log.Warn($"Config integrity check failed: {ex.Message}");
            return ConfigIntegrityResult.DecryptionFailed;
        }
    }

    // --- ICredentialRepository ---

    public CredentialStore LoadCredentialStore()
        => LoadCredentialStoreCore(CredentialsFilePath);

    public CredentialStore LoadCredentialStoreFromPath(string credentialStorePath)
        => LoadCredentialStoreCore(credentialStorePath);

    private CredentialStore LoadCredentialStoreCore(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Credential store not found.", path);

        var raw = File.ReadAllBytes(path);
        var json = Encoding.UTF8.GetString(raw);
        var store = JsonSerializer.Deserialize<CredentialStore>(json, JsonDefaults.Options)
                    ?? throw new JsonException("Failed to deserialize credential store.");

        if (store.ArgonSalt is not { Length: Constants.Argon2SaltSize })
            throw new JsonException("Credential store has invalid Argon2 salt.");
        if (store.EncryptedCanary == null || store.EncryptedCanary.Length == 0)
            throw new JsonException("Credential store has missing canary.");
        store.Credentials ??= [];

        return store;
    }

    public void SaveCredentialStore(CredentialStore store)
    {
        lock (_saveLock)
        {
            AtomicWrite(CredentialsFilePath, SerializeToBytes(store));
            log.Info("Credential store saved.");
        }
    }

    public void SaveCredentialStoreAndConfig(CredentialStore store, AppDatabase database, ISecureSecretSnapshotSource pinDerivedKey)
        => SaveCredentialStoreAndConfigCore(
            store,
            database,
            json => pinDerivedKey.TransformSnapshot(
                key => ConfigEncryptionHelper.EncryptConfig(json, key, ConfigFileType.MainConfig, store.ArgonSalt)));

    private void SaveCredentialStoreAndConfigCore(
        CredentialStore store,
        AppDatabase database,
        Func<byte[], byte[]> encryptConfig)
    {
        lock (_saveLock)
        {
            var dbToSave = ApplyFilter(database);
            var configEncrypted = SerializeAndEncrypt(dbToSave, encryptConfig);

            atomicFileWriter.AtomicWriteBatch([
                (CredentialsFilePath, SerializeToBytes(store)),
                (ConfigFilePath, configEncrypted)
            ]);
            log.Info("Credential store and config saved.");
        }
    }

    // --- IDatabaseService ---

    public AppConfig LoadAppConfigFromPath(string configPath, ISecureSecretSnapshotSource pinDerivedKey)
        => LoadAppConfigCore(
            configPath,
            raw => pinDerivedKey.TransformSnapshot(
                key => ConfigEncryptionHelper.DecryptConfig(raw, key, ConfigFileType.AppConfig)));

    private AppConfig LoadAppConfigCore(
        string configPath,
        Func<byte[], byte[]> decryptConfig)
    {
        var raw = File.ReadAllBytes(configPath);
        AppConfig config;
        byte[]? json = null;
        try
        {
            json = decryptConfig(raw);
            config = JsonSerializer.Deserialize<AppConfig>(json, JsonDefaults.Options) ?? new AppConfig();
            config.Apps ??= [];
        }
        finally
        {
            if (json != null)
                CryptographicOperations.ZeroMemory(json);
        }

        return config;
    }

    public void SaveAppConfig(AppConfig config, string configPath, ISecureSecretSnapshotSource pinDerivedKey, byte[] argonSalt)
        => SaveAppConfigCore(config, configPath, json => pinDerivedKey.TransformSnapshot(
            key => ConfigEncryptionHelper.EncryptConfig(json, key, ConfigFileType.AppConfig, argonSalt)));

    private void SaveAppConfigCore(AppConfig config, string configPath, Func<byte[], byte[]> encryptConfig)
    {
        lock (_saveLock)
        {
            var encrypted = SerializeAndEncrypt(config, encryptConfig);
            AtomicWrite(configPath, encrypted);
        }
    }

    public void SaveCredentialStoreAndAllConfigs(
        CredentialStore store,
        AppDatabase database,
        ISecureSecretSnapshotSource pinDerivedKey,
        List<(string path, AppConfig config)> additionalConfigs)
        => SaveCredentialStoreAndAllConfigsCore(
            store,
            database,
            additionalConfigs,
            json => pinDerivedKey.TransformSnapshot(
                key => ConfigEncryptionHelper.EncryptConfig(json, key, ConfigFileType.MainConfig, store.ArgonSalt)),
            json => pinDerivedKey.TransformSnapshot(
                key => ConfigEncryptionHelper.EncryptConfig(json, key, ConfigFileType.AppConfig, store.ArgonSalt)));

    private void SaveCredentialStoreAndAllConfigsCore(
        CredentialStore store,
        AppDatabase database,
        List<(string path, AppConfig config)> additionalConfigs,
        Func<byte[], byte[]> encryptMainConfig,
        Func<byte[], byte[]> encryptAdditionalConfig)
    {
        lock (_saveLock)
        {
            var files = new List<(string path, byte[] data)>
            {
                (CredentialsFilePath, SerializeToBytes(store))
            };

            var mainDb = ApplyFilter(database);
            files.Add((ConfigFilePath, SerializeAndEncrypt(mainDb, encryptMainConfig)));

            foreach (var (path, config) in additionalConfigs)
            {
                files.Add((path, SerializeAndEncrypt(config, encryptAdditionalConfig)));
            }

            atomicFileWriter.AtomicWriteBatch(files);
        }
    }

    public byte[]? TryGetConfigSalt() => TryGetConfigSaltFromPath(ConfigFilePath);

    public byte[]? TryGetConfigSaltFromPath(string configPath)
        => TryGetSaltFromPath(configPath);

    public byte[]? TryGetAppConfigSalt(string configPath)
        => TryGetAppConfigSaltFromPath(configPath);

    public byte[]? TryGetAppConfigSaltFromPath(string configPath)
        => TryGetSaltFromPath(configPath);

    private static byte[]? TryGetSaltFromPath(string configPath)
    {
        try
        {
            return ConfigEncryptionHelper.TryExtractArgonSalt(File.ReadAllBytes(configPath));
        }
        catch
        {
            return null;
        }
    }

    // --- Private helpers ---

    private AppDatabase ApplyFilter(AppDatabase database)
        => appFilter?.FilterForMainConfig(database) ?? database;

    private static byte[] SerializeToBytes<T>(T data)
        => Encoding.UTF8.GetBytes(JsonSerializer.Serialize(data, JsonDefaults.Options));

    private static byte[] SerializeAndEncrypt<T>(T data, Func<byte[], byte[]> encrypt)
    {
        byte[]? json = null;
        try
        {
            json = SerializeToBytes(data);
            return encrypt(json);
        }
        finally
        {
            if (json != null)
                CryptographicOperations.ZeroMemory(json);
        }
    }

    private void AtomicWrite(string targetPath, byte[] data)
        => atomicFileWriter.AtomicWrite(targetPath, data);
}
