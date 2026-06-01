using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.Persistence;

public interface IAppConfigPersistence
{
    AppConfig LoadAppConfigFromPath(string configPath, ISecureSecretSnapshotSource pinDerivedKey);
    void SaveAppConfig(AppConfig config, string configPath, ISecureSecretSnapshotSource pinDerivedKey, byte[] argonSalt);
}
