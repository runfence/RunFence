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
    string Password, // Plain string by design: all consumers assign this to a WinForms TextBox.Text, which is
                     // inherently a .NET string. SecureString overloads offer no benefit here since the text
                     // is immediately exposed as a string in the UI. The char[] used to generate the password
                     // is cleared in Create() immediately after constructing this string.
    List<(string Sid, string Name)> CheckedGroups,
    bool AllowLogon,
    bool AllowNetworkLogin,
    bool AllowBgAutorun,
    bool AllowInternet,
    bool AllowLan,
    bool AllowLocalhost,
    bool IsEphemeral,
    PrivilegeLevel PrivilegeLevel,
    string DesktopSettingsPath,
    List<InstallablePackage> InstallPackages)
{
    /// <summary>
    /// Creates defaults for account creation.
    /// Username is a timestamp-based unique name.
    /// Password is a randomly generated strong password.
    /// Groups default to checked=Users (Windows adds new accounts to Users automatically),
    /// unchecked=empty (no other groups are unchecked by default).
    /// Internet is allowed by default; LAN and Localhost are blocked by default (more secure isolation).
    /// </summary>
    public static AccountCreationDefaults Create(AppDatabase database, ILocalGroupMembershipService groupMembership)
    {
        var allGroups = GroupFilterHelper.FilterForCreateDialog(groupMembership.GetLocalGroups()).ToList();

        // Users group is unchecked by default: Windows adds all authenticated users to BUILTIN\Users
        // at the token level regardless of SAM membership, so explicit membership is redundant.
        // All groups start unchecked; user can add them explicitly.
        List<(string Sid, string Name)> checkedGroups = [];

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
            AllowLan: false,
            AllowLocalhost: false,
            IsEphemeral: false,
            PrivilegeLevel: PrivilegeLevel.Basic,
            DesktopSettingsPath: database.Settings.DefaultDesktopSettingsPath,
            InstallPackages: []);
    }
}