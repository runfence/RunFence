using System.Security;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Launch;
using RunFence.Security;

namespace RunFence.Account;

public static class CredentialHelper
{
    public static LaunchCredentials? DecryptAndResolve(
        string accountSid,
        CredentialStore credentialStore,
        ICredentialEncryptionService encryptionService,
        byte[] pinDerivedKey,
        ISidResolver sidResolver,
        IReadOnlyDictionary<string, string>? sidNames,
        out CredentialLookupStatus status)
    {
        status = TryDecryptCredential(accountSid, credentialStore, encryptionService, pinDerivedKey,
            out var credEntry, out var password);

        if (status is CredentialLookupStatus.NotFound or CredentialLookupStatus.MissingPassword)
            return null;

        var tokenSource = status switch
        {
            CredentialLookupStatus.CurrentAccount => LaunchTokenSource.CurrentProcess,
            CredentialLookupStatus.InteractiveUser => LaunchTokenSource.InteractiveUser,
            _ => LaunchTokenSource.Credentials
        };

        // For interactive user, try to decrypt stored password as fallback (explorer token is primary)
        if (status == CredentialLookupStatus.InteractiveUser && credEntry is { EncryptedPassword.Length: > 0 })
            password = encryptionService.Decrypt(credEntry.EncryptedPassword, pinDerivedKey);

        var (domain, username) = credEntry != null
            ? SidNameResolver.ResolveDomainAndUsername(credEntry, sidResolver, sidNames)
            : SidNameResolver.ResolveDomainAndUsername(accountSid, false, sidResolver, sidNames);

        return new LaunchCredentials(password, domain, username, tokenSource);
    }

    public static CredentialLookupStatus TryDecryptCredential(
        string accountSid,
        CredentialStore credentialStore,
        ICredentialEncryptionService encryptionService,
        byte[] pinDerivedKey,
        out CredentialEntry? credEntry,
        out SecureString? password)
    {
        credEntry = credentialStore.Credentials.FirstOrDefault(c =>
            string.Equals(c.Sid, accountSid, StringComparison.OrdinalIgnoreCase));
        password = null;

        if (credEntry == null)
        {
            // No stored credential — check if the SID belongs to the interactive user.
            // The explorer token is used for launch; no password is needed.
            var interactiveSid = SidResolutionHelper.GetInteractiveUserSid();
            if (interactiveSid != null &&
                string.Equals(accountSid, interactiveSid, StringComparison.OrdinalIgnoreCase))
                return CredentialLookupStatus.InteractiveUser;

            return CredentialLookupStatus.NotFound;
        }

        if (credEntry.IsCurrentAccount)
            return CredentialLookupStatus.CurrentAccount;

        if (credEntry.IsInteractiveUser)
            return CredentialLookupStatus.InteractiveUser;

        if (credEntry.EncryptedPassword.Length == 0)
            return CredentialLookupStatus.MissingPassword;

        password = encryptionService.Decrypt(credEntry.EncryptedPassword, pinDerivedKey);
        return CredentialLookupStatus.Success;
    }
}

public enum CredentialLookupStatus
{
    Success,
    CurrentAccount,
    InteractiveUser,
    NotFound,
    MissingPassword
}