using RunFence.Account;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Persistence;
using RunFence.Security;

namespace RunFence.RunAs;

/// <summary>
/// Handles credential persistence for newly created RunAs accounts:
/// encrypts the password, creates the credential entry, caches the SID name,
/// adds the entry to the store, and saves to disk.
/// </summary>
public class RunAsCredentialCreator(
    SessionContext session,
    ICredentialEncryptionSpanService encryptionService,
    IDatabaseService databaseService,
    ILocalUserProvider localUserProvider,
    ISidNameCacheService sidNameCache)
{
    /// <summary>
    /// Encrypts <paramref name="createdPassword"/>, creates a <see cref="CredentialEntry"/>,
    /// caches the SID display name, adds the entry to the credential store, invalidates the
    /// local user cache, and persists the store to disk.
    /// Throws <see cref="RunAsCredentialPersistenceException"/> on save failure with the
    /// prepared rollback state attached.
    /// </summary>
    public CredentialEntry PersistCredential(
        ProtectedString createdPassword,
        string createdSid,
        string username,
        CreatedAccountRollbackState rollbackState)
    {
        var encryptedPassword = session.PinDerivedKey.TransformSnapshot(key =>
            encryptionService.Encrypt(createdPassword, key));

        var newCredential = new CredentialEntry
        {
            Id = Guid.NewGuid(),
            Sid = createdSid,
            EncryptedPassword = encryptedPassword
        };

        sidNameCache.ResolveAndCache(createdSid, username);
        session.CredentialStore.Credentials.Add(newCredential);
        rollbackState.CredentialId = newCredential.Id;
        localUserProvider.InvalidateCache();

        try
        {
            databaseService.SaveCredentialStore(session.CredentialStore);
        }
        catch (Exception ex)
        {
            throw new RunAsCredentialPersistenceException(rollbackState, ex);
        }

        return newCredential;
    }
}
