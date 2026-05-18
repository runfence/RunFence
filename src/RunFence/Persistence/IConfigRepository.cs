using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.Persistence;

public interface IConfigRepository
{
    AppDatabase LoadConfig(ISecureSecretSnapshotSource pinDerivedKey);
    void SaveConfig(AppDatabase database, ISecureSecretSnapshotSource pinDerivedKey, byte[] argonSalt);
    ConfigIntegrityResult VerifyConfigIntegrity(ISecureSecretSnapshotSource pinDerivedKey);
}
