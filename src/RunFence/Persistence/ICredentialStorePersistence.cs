using RunFence.Core.Models;

namespace RunFence.Persistence;

public interface ICredentialStorePersistence
{
    CredentialStore LoadCredentialStore();
    CredentialStore LoadCredentialStoreFromPath(string credentialStorePath);
    void SaveCredentialStore(CredentialStore store);
}
