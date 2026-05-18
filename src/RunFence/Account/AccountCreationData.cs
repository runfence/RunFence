using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.Account;

/// <summary>
/// Captures all values from the account creation dialog needed to commit a new account to the database.
/// </summary>
public record AccountCreationData(
    string CreatedSid,
    ProtectedString CreatedPassword,
    string NewUsername,
    bool IsEphemeral,
    PrivilegeLevel PrivilegeLevel,
    bool FirewallSettingsChanged,
    bool AllowInternet,
    bool AllowLocalhost,
    bool AllowLan,
    bool UsersGroupUnchecked,
    bool AdminGroupChecked,
    CreatedAccountRollbackState? CreationRollbackState = null);
