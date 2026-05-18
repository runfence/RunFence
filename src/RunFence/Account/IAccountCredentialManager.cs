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
        string sid, ProtectedString password,
        CredentialStore credStore, ISecureSecretSnapshotSource pinKey);

    /// <summary>
    /// Adds a new credential from the credential edit dialog fields.
    /// Returns (success, credentialId, errorMessage). errorMessage is non-null on duplicate.
    /// </summary>
    (bool Success, Guid? CredentialId, string? Error) AddNewCredential(
        string sid, ProtectedString? password,
        CredentialStore credStore, ISecureSecretSnapshotSource pinKey);

    void UpdateCredentialPassword(CredentialEntry credEntry, ProtectedString password, ISecureSecretSnapshotSource pinKey);

    void RemoveCredential(Guid credentialId, CredentialStore credStore);

    void RemoveCredentialsBySid(string sid, CredentialStore credStore);

    /// <summary>
    /// Decrypts the stored password for the given account SID, bypassing launch-identity checks
    /// (current account, interactive user). Returns true and sets <paramref name="password"/> when
    /// a non-empty encrypted password exists; returns false with null when none is stored.
    /// Use when the actual password value is needed regardless of account type
    /// (e.g. Task Scheduler registration).
    /// </summary>
    bool TryDecryptStoredPassword(
        string accountSid, CredentialStore credStore, ISecureSecretSnapshotSource pinKey,
        out ProtectedString? password);
}
