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

    void SaveCredentialStoreAndConfig(
        CredentialStore credStore, AppDatabase database, ProtectedBuffer pinKey);

    void SaveConfig(AppDatabase database, ProtectedBuffer pinKey, byte[] argonSalt);

    /// <summary>
    /// Detects and applies stale name updates from pre-resolved SID→name mappings.
    /// Updates AppDatabase.SidNames (config only — not credential store).
    /// Returns true if any names were updated (and saved).
    /// </summary>
    bool ApplyStaleNameUpdates(
        Dictionary<string, string?> resolutions,
        AppDatabase database, ProtectedBuffer pinKey, byte[] argonSalt);
}