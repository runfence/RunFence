using RunFence.Account.UI;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.UI;

namespace RunFence.Account;

/// <summary>
/// Canonical default values for new account creation, shared between the Create Account dialog
/// and wizard templates. Both consumers initialize their UI or data from these defaults,
/// ensuring future changes (e.g. new fields, different defaults) propagate everywhere automatically.
/// </summary>
public record AccountCreationDefaults(
    string Username,
    string Password,
    List<(string Sid, string Name)> CheckedGroups,
    bool AllowLogon,
    bool AllowNetworkLogin,
    bool AllowBgAutorun,
    bool AllowInternet,
    bool AllowLan,
    bool AllowLocalhost,
    bool IsEphemeral,
    bool UseSplitToken,
    bool UseLowIntegrity,
    string DesktopSettingsPath,
    List<InstallablePackage> InstallPackages)
{
    /// <summary>
    /// Creates defaults for account creation.
    /// Username is a timestamp-based unique name.
    /// Password is a randomly generated strong password.
    /// Groups default to checked=Users (Windows adds new accounts to Users automatically),
    /// unchecked=empty (no other groups are unchecked by default).
    /// All restrictions default to the most permissive state (logon allowed, internet allowed, etc.)
    /// so that templates only need to override what they restrict.
    /// </summary>
    public static AccountCreationDefaults Create(AppDatabase database, ILocalGroupMembershipService groupMembership)
    {
        var allGroups = GroupFilterHelper.FilterForCreateDialog(groupMembership.GetLocalGroups()).ToList();

        // Users group is pre-checked (Windows auto-adds new accounts to it).
        // All other groups start unchecked (user can add them explicitly).
        var checkedGroups = allGroups
            .Where(g => string.Equals(g.Sid, GroupFilterHelper.UsersSid, StringComparison.OrdinalIgnoreCase))
            .Select(g => (g.Sid, g.Username))
            .ToList();

        // UncheckedGroups is empty — only the Users group is tracked for potential removal,
        // but it stays checked by default so there's nothing to remove.
        var uncheckedGroups = new List<(string Sid, string Name)>();

        char[]? generatedChars = null;
        string password;
        try
        {
            generatedChars = PasswordHelper.GenerateRandomPassword();
            password = new string(generatedChars);
        }
        finally
        {
            if (generatedChars != null)
                Array.Clear(generatedChars, 0, generatedChars.Length);
        }

        return new AccountCreationDefaults(
            Username: $"u{DateTime.Now:yyMMddHHmm}",
            Password: password,
            CheckedGroups: checkedGroups,
            AllowLogon: false,
            AllowNetworkLogin: false,
            AllowBgAutorun: false,
            AllowInternet: true,
            AllowLan: true,
            AllowLocalhost: true,
            IsEphemeral: false,
            UseSplitToken: true,
            UseLowIntegrity: false,
            DesktopSettingsPath: database.Settings.DefaultDesktopSettingsPath,
            InstallPackages: []);
    }
}