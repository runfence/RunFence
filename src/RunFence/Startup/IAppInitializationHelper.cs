using RunFence.Core.Models;

namespace RunFence.Startup;

/// <summary>
/// Startup initialization helpers used by Program.cs and LockManager's PIN reset flow.
/// </summary>
public interface IAppInitializationHelper
{
    /// <summary>
    /// Ensures the current account has a credential entry in the store.
    /// Optionally updates the SidNames map with the current account's display name.
    /// Returns true if a credential was added (caller should save).
    /// </summary>
    bool EnsureCurrentAccountCredential(CredentialStore credentialStore, AppDatabase? database = null);

    /// <summary>
    /// Updates the SidNames map with the interactive user's display name if a different user
    /// is currently interactively logged in. No credential entry is created.
    /// </summary>
    void EnsureInteractiveUserSidName(AppDatabase database);

    /// <summary>
    /// Sets empty AccountSid to the current account SID for all apps that are not AppContainer entries.
    /// Returns true if any changes were made (caller should save).
    /// </summary>
    bool NormalizeAccountSids(IList<AppEntry> apps, string currentAccountSid);

    /// <summary>
    /// Initializes a freshly created <see cref="AppDatabase"/> for first-run and reset flows:
    /// populates default IPC callers, ensures the interactive user SID name is recorded,
    /// and ensures the SYSTEM account entry is present with the correct privilege level.
    /// </summary>
    void InitializeNewDatabase(AppDatabase database);
}