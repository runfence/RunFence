using RunFence.Core.Models;

namespace RunFence.Persistence;

public interface IConfigRepository
{
    AppDatabase LoadConfig(byte[] pinDerivedKey);
    void SaveConfig(AppDatabase database, byte[] pinDerivedKey, byte[] argonSalt);
    ConfigIntegrityResult VerifyConfigIntegrity(byte[] pinDerivedKey);
}