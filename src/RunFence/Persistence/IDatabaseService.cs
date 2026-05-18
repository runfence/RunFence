using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.Persistence;

public enum ConfigIntegrityResult
{
    Valid,
    FirstRun,
    DecryptionFailed
}

public interface IDatabaseService : IConfigRepository, ICredentialRepository
{
    AppDatabase LoadConfigFromPath(string configPath, ISecureSecretSnapshotSource pinDerivedKey);
    CredentialStore LoadCredentialStoreFromPath(string credentialStorePath);
    AppConfig LoadAppConfigFromPath(string configPath, ISecureSecretSnapshotSource pinDerivedKey);
    void SaveAppConfig(AppConfig config, string configPath, ISecureSecretSnapshotSource pinDerivedKey, byte[] argonSalt);
    void SaveCredentialStoreAndAllConfigs(CredentialStore store, AppDatabase database,
        ISecureSecretSnapshotSource pinDerivedKey, List<(string path, AppConfig config)> additionalConfigs);
    byte[]? TryGetConfigSalt();
    byte[]? TryGetConfigSaltFromPath(string configPath);
    byte[]? TryGetAppConfigSalt(string configPath);
    byte[]? TryGetAppConfigSaltFromPath(string configPath);
}
