using RunFence.Core;
using RunFence.Core.Helpers;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Launch;
using RunFence.Security;

namespace RunFence.Account;

public class CredentialDecryptionService(
    ICredentialEncryptionSpanService spanEncryptionService,
    ISidResolver sidResolver,
    IInteractiveUserSidResolver interactiveUserSidResolver)
    : ICredentialDecryptionService
{
    public LaunchCredentials? DecryptAndResolve(
        string accountSid,
        CredentialStore credentialStore,
        ReadOnlySpan<byte> pinDerivedKey,
        IReadOnlyDictionary<string, string>? sidNames,
        out CredentialLookupStatus status)
        => DecryptAndResolveCore(accountSid, credentialStore, pinDerivedKey, sidNames, out status);

    public CredentialLookupStatus TryDecryptCredential(
        string accountSid,
        CredentialStore credentialStore,
        ReadOnlySpan<byte> pinDerivedKey,
        out CredentialEntry? credEntry,
        out ProtectedString? password)
        => TryDecryptCredentialCore(accountSid, credentialStore, pinDerivedKey, out credEntry, out password);

    public CredentialLookupStatus CheckCredential(string accountSid, CredentialStore credentialStore) =>
        ResolvePreDecryptStatus(accountSid, credentialStore, out _) ?? CredentialLookupStatus.Success;

    private LaunchCredentials? DecryptAndResolveCore(
        string accountSid,
        CredentialStore credentialStore,
        ReadOnlySpan<byte> pinDerivedKey,
        IReadOnlyDictionary<string, string>? sidNames,
        out CredentialLookupStatus status)
    {
        status = TryDecryptCredentialCore(accountSid, credentialStore, pinDerivedKey,
            out var credEntry, out var password);

        if (status is CredentialLookupStatus.NotFound or CredentialLookupStatus.MissingPassword)
            return null;

        var tokenSource = status switch
        {
            CredentialLookupStatus.CurrentAccount => LaunchTokenSource.CurrentProcess,
            CredentialLookupStatus.InteractiveUser => LaunchTokenSource.InteractiveUser,
            CredentialLookupStatus.SystemAccount => LaunchTokenSource.SystemAccount,
            _ => LaunchTokenSource.Credentials
        };

        if (status == CredentialLookupStatus.InteractiveUser && credEntry is { EncryptedPassword.Length: > 0 })
            password = spanEncryptionService.Decrypt(credEntry.EncryptedPassword, pinDerivedKey);

        var (domain, username) = credEntry != null
            ? SidNameResolver.ResolveDomainAndUsername(credEntry, sidResolver, sidNames)
            : SidNameResolver.ResolveDomainAndUsername(accountSid, false, sidResolver, sidNames);

        return new LaunchCredentials(password, domain, username, tokenSource);
    }

    private CredentialLookupStatus TryDecryptCredentialCore(
        string accountSid,
        CredentialStore credentialStore,
        ReadOnlySpan<byte> pinDerivedKey,
        out CredentialEntry? credEntry,
        out ProtectedString? password)
    {
        var preDecryptStatus = ResolvePreDecryptStatus(accountSid, credentialStore, out credEntry);
        password = null;

        if (preDecryptStatus != null)
            return preDecryptStatus.Value;

        password = spanEncryptionService.Decrypt(credEntry!.EncryptedPassword, pinDerivedKey);
        return CredentialLookupStatus.Success;
    }

    /// <summary>
    /// Resolves the credential lookup status for all cases that do not require decryption.
    /// Returns null when the credential is present and has an encrypted password that must be decrypted
    /// to confirm success; returns a non-null status for all other cases.
    /// </summary>
    private CredentialLookupStatus? ResolvePreDecryptStatus(
        string accountSid, CredentialStore credentialStore, out CredentialEntry? credEntry)
    {
        var interactiveSid = interactiveUserSidResolver.GetInteractiveUserSid();

        if (SidResolutionHelper.IsSystemSid(accountSid))
        {
            credEntry = null;
            return CredentialLookupStatus.SystemAccount;
        }

        credEntry = credentialStore.Credentials.FirstOrDefault(c =>
            SidComparer.SidEquals(c.Sid, accountSid));

        if (credEntry == null)
        {
            // No stored credential - check if the SID belongs to the interactive user.
            // The explorer token is used for launch; no password is needed.
            if (interactiveSid != null &&
                SidComparer.SidEquals(accountSid, interactiveSid))
                return CredentialLookupStatus.InteractiveUser;

            return CredentialLookupStatus.NotFound;
        }

        if (credEntry.IsCurrentAccount)
            return CredentialLookupStatus.CurrentAccount;

        if (interactiveSid != null &&
            !SidComparer.SidEquals(credEntry.Sid, SidResolutionHelper.GetCurrentUserSid()) &&
            SidComparer.SidEquals(credEntry.Sid, interactiveSid))
            return CredentialLookupStatus.InteractiveUser;

        if (credEntry.EncryptedPassword.Length == 0)
            return CredentialLookupStatus.MissingPassword;

        return null;
    }
}
