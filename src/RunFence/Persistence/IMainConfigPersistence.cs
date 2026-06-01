using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.Persistence;

public interface IMainConfigPersistence
{
    AppDatabase LoadConfig(ISecureSecretSnapshotSource pinDerivedKey);
    AppDatabase LoadConfigFromPath(string configPath, ISecureSecretSnapshotSource pinDerivedKey);
    void SaveConfig(AppDatabase database, ISecureSecretSnapshotSource pinDerivedKey, byte[] argonSalt);
}
