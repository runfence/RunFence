using System.Security;
using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.Account;

/// <summary>
/// Manages credential storage, retrieval, and encryption operations for local accounts.
/// </summary>
public interface IAccountCredentialManager
{
    /// <summary>
    /// Stores a credential for a newly created user. Returns the credential ID,
    /// or null if a credential with the same SID already exists (duplicate guard).
    /// </summary>
    Guid? StoreCreatedUserCredential(
        string sid, SecureString password,
        CredentialStore credStore, ProtectedBuffer pinKey);

    /// <summary>
    /// Adds a new credential from the credential edit dialog fields.
    /// Returns (success, credentialId, errorMessage). errorMessage is non-null on duplicate.
    /// </summary>
    (bool Success, Guid? CredentialId, string? Error) AddNewCredential(
        string sid, SecureString? password,
        CredentialStore credStore, ProtectedBuffer pinKey);

    void UpdateCredentialPassword(CredentialEntry credEntry, SecureString password, ProtectedBuffer pinKey);

    void RemoveCredential(Guid credentialId, CredentialStore credStore);

    void RemoveCredentialsBySid(string sid, CredentialStore credStore);

    CredentialLookupStatus DecryptCredential(
        string accountSid, CredentialStore credStore, ProtectedBuffer pinKey,
        out SecureString? password);

    /// <summary>
    /// Checks whether valid credentials exist for the account without decrypting the password.
    /// Use this when only the lookup status is needed.
    /// </summary>
    CredentialLookupStatus CheckCredential(string accountSid, CredentialStore credStore);
}
