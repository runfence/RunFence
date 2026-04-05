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
    /// Pre-populates AllowedIpcCallers with the current process owner SID and the interactive
    /// desktop user SID (explorer.exe owner), used on first run and reset flows.
    /// </summary>
    void PopulateDefaultIpcCallers(AppDatabase database);
}