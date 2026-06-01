using RunFence.Core;

namespace RunFence.Persistence;

public interface IConfigIntegrityVerifier
{
    ConfigIntegrityResult VerifyConfigIntegrity(ISecureSecretSnapshotSource pinDerivedKey);
}
