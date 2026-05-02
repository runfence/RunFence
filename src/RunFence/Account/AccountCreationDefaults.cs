using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.Account;

/// <summary>
/// Canonical default values for new account creation, shared between the Create Account dialog
/// and wizard templates. Both consumers initialize their UI or data from these defaults,
/// ensuring future changes (e.g. new fields, different defaults) propagate everywhere automatically.
/// Implements IDisposable because Password is a ProtectedString that must be disposed after use.
/// </summary>
public record AccountCreationDefaults(
    string Username,
    ProtectedString Password,
    List<(string Sid, string Name)> CheckedGroups,
    bool AllowLogon,
    bool AllowNetworkLogin,
    bool AllowBgAutorun,
    bool AllowInternet,
    bool AllowLan,
    bool AllowLocalhost,
    bool IsEphemeral,
    PrivilegeLevel PrivilegeLevel,
    string DesktopSettingsPath) : IDisposable
{
    public void Dispose() => Password.Dispose();

    /// <summary>
    /// Creates defaults for account creation.
    /// Username is a timestamp-based unique name.
    /// Password is a randomly generated strong password.
    /// Groups default to checked=Users (Windows adds new accounts to Users automatically),
    /// unchecked=empty (no other groups are unchecked by default).
    /// Internet is allowed by default; LAN and Localhost are blocked by default (more secure isolation).
    /// </summary>
    public static AccountCreationDefaults Create(AppDatabase database)
    {
        // Users group is unchecked by default: Windows adds all authenticated users to BUILTIN\Users
        // at the token level regardless of SAM membership, so explicit membership is redundant.
        // All groups start unchecked; user can add them explicitly.
        List<(string Sid, string Name)> checkedGroups = [];

        char[]? generatedChars = null;
        ProtectedString password;
        try
        {
            generatedChars = PasswordHelper.GenerateRandomPassword();
            password = ProtectedString.FromChars(generatedChars);
        }
        finally
        {
            if (generatedChars != null)
                Array.Clear(generatedChars, 0, generatedChars.Length);
        }

        return new AccountCreationDefaults(
            // Minute-level granularity is by design: Windows usernames are limited to 20 chars, and
            // two accounts created in the same minute are rare enough that collisions can be resolved manually.
            Username: $"u{DateTime.Now:yyMMddHHmm}",
            Password: password,
            CheckedGroups: checkedGroups,
            AllowLogon: false,
            AllowNetworkLogin: false,
            AllowBgAutorun: false,
            AllowInternet: true,
            AllowLan: false,
            AllowLocalhost: false,
            IsEphemeral: false,
            PrivilegeLevel: PrivilegeLevel.Basic,
            DesktopSettingsPath: database.Settings.DefaultDesktopSettingsPath);
    }
}