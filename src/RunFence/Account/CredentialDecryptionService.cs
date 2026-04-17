using System.Security;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Launch;
using RunFence.Security;

namespace RunFence.Account;

public class CredentialDecryptionService(
    ICredentialEncryptionService encryptionService,
    ISidResolver sidResolver)
    : ICredentialDecryptionService
{
    public LaunchCredentials? DecryptAndResolve(
        string accountSid,
        CredentialStore credentialStore,
        byte[] pinDerivedKey,
        IReadOnlyDictionary<string, string>? sidNames,
        out CredentialLookupStatus status)
    {
        status = TryDecryptCredential(accountSid, credentialStore, pinDerivedKey,
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

    public CredentialLookupStatus TryDecryptCredential(
        string accountSid,
        CredentialStore credentialStore,
        byte[] pinDerivedKey,
        out CredentialEntry? credEntry,
        out SecureString? password)
    {
        var preDecryptStatus = ResolvePreDecryptStatus(accountSid, credentialStore, out credEntry);
        password = null;

        if (preDecryptStatus != null)
            return preDecryptStatus.Value;

        password = encryptionService.Decrypt(credEntry!.EncryptedPassword, pinDerivedKey);
        return CredentialLookupStatus.Success;
    }

    public CredentialLookupStatus CheckCredential(string accountSid, CredentialStore credentialStore) =>
        ResolvePreDecryptStatus(accountSid, credentialStore, out _) ?? CredentialLookupStatus.Success;

    /// <summary>
    /// Resolves the credential lookup status for all cases that do not require decryption.
    /// Returns null when the credential is present and has an encrypted password that must be decrypted
    /// to confirm success; returns a non-null status for all other cases.
    /// </summary>
    private CredentialLookupStatus? ResolvePreDecryptStatus(
        string accountSid, CredentialStore credentialStore, out CredentialEntry? credEntry)
    {
        credEntry = credentialStore.Credentials.FirstOrDefault(c =>
            string.Equals(c.Sid, accountSid, StringComparison.OrdinalIgnoreCase));

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

        return null;
    }
}
