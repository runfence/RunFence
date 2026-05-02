using RunFence.Core.Models;

namespace RunFence.Account;

internal static class CredentialStoreCloneHelper
{
    public static CredentialStore CloneStore(CredentialStore store)
        => new()
        {
            ArgonSalt = store.ArgonSalt.ToArray(),
            EncryptedCanary = store.EncryptedCanary.ToArray(),
            Credentials = store.Credentials.Select(CloneEntry).ToList()
        };

    public static CredentialEntry CloneEntry(CredentialEntry entry)
        => new()
        {
            Id = entry.Id,
            Sid = entry.Sid,
            EncryptedPassword = entry.EncryptedPassword.ToArray()
        };
}
