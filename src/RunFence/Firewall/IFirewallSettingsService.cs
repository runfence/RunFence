using RunFence.Core.Models;

namespace RunFence.Firewall;

/// <summary>
/// Provides database and username resolution needed for firewall settings operations.
/// </summary>
public interface IFirewallSettingsService
{
    /// <summary>
    /// Returns the current database and the display username for the given SID
    /// (falls back to the SID string when no name is found).
    /// </summary>
    (AppDatabase Database, string Username) GetDatabaseAndUsername(string sid);
}
