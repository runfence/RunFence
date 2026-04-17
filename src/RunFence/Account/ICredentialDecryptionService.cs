using System.Security;
using RunFence.Core.Models;
using RunFence.Launch;

namespace RunFence.Account;

public interface ICredentialDecryptionService
{
    LaunchCredentials? DecryptAndResolve(
        string accountSid,
        CredentialStore credentialStore,
        byte[] pinDerivedKey,
        IReadOnlyDictionary<string, string>? sidNames,
        out CredentialLookupStatus status);

    CredentialLookupStatus TryDecryptCredential(
        string accountSid,
        CredentialStore credentialStore,
        byte[] pinDerivedKey,
        out CredentialEntry? credEntry,
        out SecureString? password);

    /// <summary>
    /// Returns the lookup status for the account without decrypting the password.
    /// Use this when only status is needed and the password is not required.
    /// </summary>
    CredentialLookupStatus CheckCredential(string accountSid, CredentialStore credentialStore);
}
