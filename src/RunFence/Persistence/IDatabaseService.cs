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
    AppConfig LoadAppConfig(string configPath, byte[] pinDerivedKey);
    void SaveAppConfig(AppConfig config, string configPath, byte[] pinDerivedKey, byte[] argonSalt);

    void SaveCredentialStoreAndAllConfigs(CredentialStore store, AppDatabase database,
        byte[] pinDerivedKey, List<(string path, AppConfig config)> additionalConfigs);

    byte[]? TryGetConfigSalt();
    byte[]? TryGetAppConfigSalt(string configPath);
}