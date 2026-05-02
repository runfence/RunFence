using RunFence.Core;
using RunFence.Account;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Persistence;
using RunFence.Security;

namespace RunFence.RunAs;

/// <summary>
/// Handles credential persistence for newly created RunAs accounts:
/// encrypts the password, creates the credential entry, caches the SID name,
/// adds the credential to the store, and saves to disk.
/// </summary>
public class RunAsCredentialCreator(
    SessionContext session,
    ICredentialEncryptionService encryptionService,
    IDatabaseService databaseService,
    ILocalUserProvider localUserProvider,
    ISidNameCacheService sidNameCache)
{
    /// <summary>
    /// Encrypts <paramref name="createdPassword"/>, creates a <see cref="CredentialEntry"/>,
    /// caches the SID display name, adds the entry to the credential store, invalidates the
    /// local user cache, and persists the store to disk.
    /// Throws on save failure — callers are responsible for ephemeral cleanup.
    /// </summary>
    public CredentialEntry PersistCredential(
        ProtectedString createdPassword, string createdSid, string username)
    {
        using var scope = session.PinDerivedKey.Unprotect();
        var encryptedPassword = encryptionService.Encrypt(createdPassword, scope.Data);

        var newCredential = new CredentialEntry
        {
            Id = Guid.NewGuid(),
            Sid = createdSid,
            EncryptedPassword = encryptedPassword
        };

        sidNameCache.ResolveAndCache(createdSid, username);
        session.CredentialStore.Credentials.Add(newCredential);
        localUserProvider.InvalidateCache();
        databaseService.SaveCredentialStore(session.CredentialStore);

        return newCredential;
    }
}
