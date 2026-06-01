using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.Persistence;

public interface IConfigReencryptionPersistence
{
    void SaveCredentialStoreAndConfig(CredentialStore store, AppDatabase database, ISecureSecretSnapshotSource pinDerivedKey);
    void SaveCredentialStoreAndAllConfigs(
        CredentialStore store,
        AppDatabase database,
        ISecureSecretSnapshotSource pinDerivedKey,
        List<(string path, AppConfig config)> additionalConfigs);
}
