using System.Security.Cryptography;
using RunFence.Core;
using RunFence.Core.Helpers;
using RunFence.Core.Models;
using RunFence.Security;

namespace RunFence.Account;

public class AccountCredentialManager(
    ICredentialEncryptionService encryptionService)
    : IAccountCredentialManager
{
    /// <summary>
    /// Stores a credential for a newly created user. Returns the credential ID,
    /// or null if a credential with the same SID already exists (duplicate guard).
    /// </summary>
    public Guid? StoreCreatedUserCredential(
        string sid, ProtectedString password,
        CredentialStore credStore, ProtectedBuffer pinKey)
    {
        if (credStore.Credentials.Any(c => SidComparer.SidEquals(c.Sid, sid)))
            return null;

        var id = Guid.NewGuid();
        byte[] encryptedPassword;
        using (var scope = pinKey.Unprotect())
            encryptedPassword = encryptionService.Encrypt(password, scope.Data);

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
        CredentialStore credStore, ProtectedBuffer pinKey)
    {
        if (credStore.Credentials.Any(c =>
                SidComparer.SidEquals(c.Sid, sid)))
        {
            return (false, null, "A credential with this account already exists.");
        }

        var id = Guid.NewGuid();
        byte[] encryptedPassword = Array.Empty<byte>();

        if (password != null)
        {
            using var scope = pinKey.Unprotect();
            encryptedPassword = encryptionService.Encrypt(password, scope.Data);
        }

        credStore.Credentials.Add(new CredentialEntry
        {
            Id = id,
            Sid = sid,
            EncryptedPassword = encryptedPassword
        });

        return (true, id, null);
    }

    public void UpdateCredentialPassword(CredentialEntry credEntry, ProtectedString password, ProtectedBuffer pinKey)
    {
        using var scope = pinKey.Unprotect();
        credEntry.EncryptedPassword = encryptionService.Encrypt(password, scope.Data);
    }

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
        string accountSid, CredentialStore credStore, ProtectedBuffer pinKey,
        out ProtectedString? password)
    {
        var credential = credStore.Credentials.FirstOrDefault(c =>
            SidComparer.SidEquals(c.Sid, accountSid));

        if (credential == null || credential.EncryptedPassword.Length == 0)
        {
            password = null;
            return false;
        }

        using var scope = pinKey.Unprotect();
        try
        {
            password = encryptionService.Decrypt(credential.EncryptedPassword, scope.Data);
            return true;
        }
        catch (CryptographicException)
        {
            password = null;
            return false;
        }
    }
}
