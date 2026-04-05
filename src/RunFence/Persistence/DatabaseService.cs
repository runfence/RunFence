using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.Persistence;

public class DatabaseService(
    ILoggingService log,
    IAppFilter? appFilter = null,
    bool allowPlaintextConfig = false,
    string? configDir = null,
    string? localDataDir = null)
    : IDatabaseService
{
    private readonly Lock _saveLock = new();

    private string ConfigFilePath
    {
        get => Path.Combine(field, "config.dat");
    } = configDir ?? Constants.RoamingAppDataDir;

    private string CredentialsFilePath
    {
        get => Path.Combine(field, "credentials.dat");
    } = localDataDir ?? Constants.LocalAppDataDir;

    // --- IConfigRepository ---

    public AppDatabase LoadConfig(byte[] pinDerivedKey)
    {
        var path = ConfigFilePath;
        if (!File.Exists(path))
        {
            log.Info("Config file not found, returning empty database.");
            return new AppDatabase();
        }

        var raw = File.ReadAllBytes(path);

        byte[] json;
        if (ConfigEncryptionHelper.HasEncryptionHeader(raw))
        {
            json = ConfigEncryptionHelper.DecryptConfig(raw, pinDerivedKey, ConfigFileType.MainConfig);
        }
        else if (allowPlaintextConfig)
        {
            json = raw;
        }
        else
        {
            throw new CryptographicException("Config file is not encrypted.");
        }

        var db = JsonSerializer.Deserialize<AppDatabase>(json, JsonDefaults.Options) ?? new AppDatabase();
        db.Apps ??= [];
        db.Accounts ??= [];
        db.Settings ??= new();
        return db;
    }

    public void SaveConfig(AppDatabase database, byte[] pinDerivedKey, byte[] argonSalt)
    {
        lock (_saveLock)
        {
            var dbToSave = ApplyFilter(database);
            var json = SerializeToBytes(dbToSave);
            var encrypted = ConfigEncryptionHelper.EncryptConfig(json, pinDerivedKey, ConfigFileType.MainConfig, argonSalt);
            EnsureDirectory(ConfigFilePath);
            AtomicWrite(ConfigFilePath, encrypted);
            log.Info("Config saved (encrypted).");
        }
    }

    public ConfigIntegrityResult VerifyConfigIntegrity(byte[] pinDerivedKey)
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
            ConfigEncryptionHelper.DecryptConfig(raw, pinDerivedKey, ConfigFileType.MainConfig);
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
    {
        var path = CredentialsFilePath;

        if (!File.Exists(path))
            throw new FileNotFoundException("Credential store not found.", path);

        var json = File.ReadAllText(path);
        var store = JsonSerializer.Deserialize<CredentialStore>(json, JsonDefaults.Options)
                    ?? throw new JsonException("Failed to deserialize credential store.");

        if (store.ArgonSalt == null || store.ArgonSalt.Length != Constants.Argon2SaltSize)
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
            EnsureDirectory(CredentialsFilePath);
            AtomicWrite(CredentialsFilePath, SerializeToBytes(store));
            log.Info("Credential store saved.");
        }
    }

    public void SaveCredentialStoreAndConfig(CredentialStore store, AppDatabase database, byte[] pinDerivedKey)
    {
        lock (_saveLock)
        {
            var dbToSave = ApplyFilter(database);
            var configEncrypted = ConfigEncryptionHelper.EncryptConfig(
                SerializeToBytes(dbToSave), pinDerivedKey, ConfigFileType.MainConfig, store.ArgonSalt);

            AtomicWriteBatch([
                (CredentialsFilePath, SerializeToBytes(store)),
                (ConfigFilePath, configEncrypted)
            ]);
            log.Info("Credential store and config saved.");
        }
    }

    // --- IDatabaseService ---

    public AppConfig LoadAppConfig(string configPath, byte[] pinDerivedKey)
    {
        var raw = File.ReadAllBytes(configPath);
        var json = ConfigEncryptionHelper.DecryptConfig(raw, pinDerivedKey, ConfigFileType.AppConfig);
        var config = JsonSerializer.Deserialize<AppConfig>(json, JsonDefaults.Options) ?? new AppConfig();
        config.Apps ??= [];
        return config;
    }

    public void SaveAppConfig(AppConfig config, string configPath, byte[] pinDerivedKey, byte[] argonSalt)
    {
        lock (_saveLock)
        {
            var json = SerializeToBytes(config);
            var encrypted = ConfigEncryptionHelper.EncryptConfig(json, pinDerivedKey, ConfigFileType.AppConfig, argonSalt);
            EnsureDirectory(configPath);
            AtomicWrite(configPath, encrypted);
        }
    }

    public void SaveCredentialStoreAndAllConfigs(CredentialStore store, AppDatabase database,
        byte[] pinDerivedKey, List<(string path, AppConfig config)> additionalConfigs)
    {
        lock (_saveLock)
        {
            var files = new List<(string path, byte[] data)> {
                // Credentials FIRST — if Phase 2 fails after credentials but before configs,
                // new PIN works against credential store, and old configs fail with DecryptionFailed
                // which is the existing recoverable startup flow ("Start Fresh" / "Exit").
                (CredentialsFilePath, SerializeToBytes(store)) };

            // Main config (filtered) — uses new store's salt
            var mainDb = ApplyFilter(database);
            files.Add((ConfigFilePath, ConfigEncryptionHelper.EncryptConfig(
                SerializeToBytes(mainDb), pinDerivedKey, ConfigFileType.MainConfig, store.ArgonSalt)));

            // Additional configs — use new store's salt
            foreach (var (path, config) in additionalConfigs)
                files.Add((path, ConfigEncryptionHelper.EncryptConfig(
                    SerializeToBytes(config), pinDerivedKey, ConfigFileType.AppConfig, store.ArgonSalt)));

            AtomicWriteBatch(files);
        }
    }

    public byte[]? TryGetConfigSalt() => TryGetAppConfigSalt(ConfigFilePath);

    public byte[]? TryGetAppConfigSalt(string configPath)
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

    /// <summary>
    /// Two-phase atomic write with rollback.
    /// Phase 1: Write all .tmp files.
    /// Phase 2: Replace all atomically per-file (in provided order).
    /// Rollback: On Phase 2 failure, restore already-replaced files from .bak
    /// and delete any newly-created files (those that had no prior target).
    /// </summary>
    private static void AtomicWriteBatch(List<(string path, byte[] data)> files)
    {
        var tmpFiles = new List<(string tmpPath, string targetPath)>();
        var replacedFiles = new List<(string targetPath, string bakPath)>();
        var createdFiles = new List<string>();
        try
        {
            // Phase 1: Write all .tmp files
            foreach (var (path, data) in files)
            {
                EnsureDirectory(path);
                var dir = Path.GetDirectoryName(path)!;
                var tmpPath = Path.Combine(dir, $"{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
                File.WriteAllBytes(tmpPath, data);
                tmpFiles.Add((tmpPath, path));
            }

            // Phase 2: Replace all atomically per-file
            foreach (var (tmpPath, targetPath) in tmpFiles)
            {
                var bakPath = targetPath + ".bak";
                if (File.Exists(targetPath))
                {
                    File.Replace(tmpPath, targetPath, bakPath);
                    replacedFiles.Add((targetPath, bakPath));
                }
                else
                {
                    File.Move(tmpPath, targetPath);
                    createdFiles.Add(targetPath);
                }
            }
        }
        catch
        {
            // Rollback: restore already-replaced files from .bak
            foreach (var (targetPath, bakPath) in replacedFiles)
            {
                try
                {
                    if (File.Exists(bakPath))
                        File.Move(bakPath, targetPath, overwrite: true);
                }
                catch
                {
                } // best-effort restore; continue rollback of remaining files
            }

            // Rollback: delete newly-created files (no backup to restore)
            foreach (var createdPath in createdFiles)
            {
                try
                {
                    File.Delete(createdPath);
                }
                catch
                {
                } // best-effort cleanup
            }

            // Cleanup: delete remaining .tmp files
            foreach (var (tmpPath, _) in tmpFiles)
            {
                try
                {
                    if (File.Exists(tmpPath))
                        File.Delete(tmpPath);
                }
                catch
                {
                } // best-effort cleanup
            }

            throw;
        }
    }

    private static void AtomicWrite(string targetPath, byte[] data)
    {
        var dir = Path.GetDirectoryName(targetPath)!;
        var tmpPath = Path.Combine(dir, $"{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp");
        var bakPath = targetPath + ".bak";

        File.WriteAllBytes(tmpPath, data);

        if (File.Exists(targetPath))
        {
            File.Replace(tmpPath, targetPath, bakPath);
        }
        else
        {
            File.Move(tmpPath, targetPath);
        }
    }

    private static void EnsureDirectory(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (dir != null && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
    }
}