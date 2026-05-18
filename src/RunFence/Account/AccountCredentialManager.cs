using System.Security.Cryptography;
using RunFence.Core;
using RunFence.Core.Helpers;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Security;

namespace RunFence.Account;

public class AccountCredentialManager(
    ICredentialEncryptionSpanService encryptionService)
    : IAccountCredentialManager
{
    /// <summary>
    /// Stores a credential for a newly created user. Returns the credential ID,
    /// or null if a credential with the same SID already exists (duplicate guard).
    /// </summary>
    public Guid? StoreCreatedUserCredential(
        string sid, ProtectedString password,
        CredentialStore credStore, ISecureSecretSnapshotSource pinKey)
    {
        if (credStore.Credentials.Any(c => SidComparer.SidEquals(c.Sid, sid)))
            return null;

        var id = Guid.NewGuid();
        var encryptedPassword = pinKey.TransformSnapshot(key => encryptionService.Encrypt(password, key));

        credStore.Credentials.Add(new CredentialEntry
        {
            Id = id,
            Sid = sid,
            EncryptedPassword = encryptedPassword
        });

        return id;
    }

    /// <summary>
    /// Adds a new credential from the credential edit dialog fields.
    /// Returns (success, errorMessage). errorMessage is non-null on duplicate.
    /// </summary>
    public (bool Success, Guid? CredentialId, string? Error) AddNewCredential(
        string sid, ProtectedString? password,
        CredentialStore credStore, ISecureSecretSnapshotSource pinKey)
    {
        if (credStore.Credentials.Any(c =>
                SidComparer.SidEquals(c.Sid, sid)))
        {
            return (false, null, "A credential with this account already exists.");
        }

        var id = Guid.NewGuid();
        byte[] encryptedPassword = Array.Empty<byte>();

        if (password != null)
            encryptedPassword = pinKey.TransformSnapshot(key => encryptionService.Encrypt(password, key));

        credStore.Credentials.Add(new CredentialEntry
        {
            Id = id,
            Sid = sid,
            EncryptedPassword = encryptedPassword
        });

        return (true, id, null);
    }

    public void UpdateCredentialPassword(CredentialEntry credEntry, ProtectedString password, ISecureSecretSnapshotSource pinKey)
        => credEntry.EncryptedPassword = pinKey.TransformSnapshot(key => encryptionService.Encrypt(password, key));

    public void RemoveCredential(Guid credentialId, CredentialStore credStore)
    {
        credStore.Credentials.RemoveAll(c => c.Id == credentialId);
    }

    public void RemoveCredentialsBySid(string sid, CredentialStore credStore)
    {
        credStore.Credentials.RemoveAll(c =>
            SidComparer.SidEquals(c.Sid, sid));
    }

    public bool TryDecryptStoredPassword(
        string accountSid, CredentialStore credStore, ISecureSecretSnapshotSource pinKey,
        out ProtectedString? password)
    {
        var credential = credStore.Credentials.FirstOrDefault(c =>
            SidComparer.SidEquals(c.Sid, accountSid));

        if (credential == null || credential.EncryptedPassword.Length == 0)
        {
            password = null;
            return false;
        }

        var result = pinKey.TransformSnapshot(key =>
        {
            try
            {
                return (success: true, password: encryptionService.Decrypt(credential.EncryptedPassword, key));
            }
            catch (CryptographicException)
            {
                return (success: false, password: (ProtectedString?)null);
            }
        });
        password = result.password;
        return result.success;
    }

}
