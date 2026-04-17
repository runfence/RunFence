using System.Security;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Security;

namespace RunFence.Account;

public class AccountCredentialManager(
    ICredentialEncryptionService encryptionService,
    ICredentialDecryptionService credentialDecryption)
    : IAccountCredentialManager
{
    /// <summary>
    /// Stores a credential for a newly created user. Returns the credential ID,
    /// or null if a credential with the same SID already exists (duplicate guard).
    /// </summary>
    public Guid? StoreCreatedUserCredential(
        string sid, SecureString password,
        CredentialStore credStore, ProtectedBuffer pinKey)
    {
        if (credStore.Credentials.Any(c => string.Equals(c.Sid, sid, StringComparison.OrdinalIgnoreCase)))
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
        string sid, SecureString? password,
        CredentialStore credStore, ProtectedBuffer pinKey)
    {
        if (credStore.Credentials.Any(c =>
                string.Equals(c.Sid, sid, StringComparison.OrdinalIgnoreCase)))
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

    public void UpdateCredentialPassword(CredentialEntry credEntry, SecureString password, ProtectedBuffer pinKey)
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
            string.Equals(c.Sid, sid, StringComparison.OrdinalIgnoreCase));
    }

    public CredentialLookupStatus DecryptCredential(
        string accountSid, CredentialStore credStore, ProtectedBuffer pinKey,
        out SecureString? password)
    {
        using var scope = pinKey.Unprotect();
        return credentialDecryption.TryDecryptCredential(
            accountSid, credStore, scope.Data,
            out _, out password);
    }

    public CredentialLookupStatus CheckCredential(string accountSid, CredentialStore credStore) =>
        credentialDecryption.CheckCredential(accountSid, credStore);
}
