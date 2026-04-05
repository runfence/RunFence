using RunFence.Core.Models;

namespace RunFence.Persistence;

public interface ICredentialRepository
{
    CredentialStore LoadCredentialStore();
    void SaveCredentialStore(CredentialStore store);
    void SaveCredentialStoreAndConfig(CredentialStore store, AppDatabase database, byte[] pinDerivedKey);
}