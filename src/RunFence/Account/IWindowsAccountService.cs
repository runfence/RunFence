using RunFence.Core;

namespace RunFence.Account;

public interface IWindowsAccountService
{
    void DeleteUser(string sid);
    string CreateLocalUser(string username, ProtectedString password);
    string? GetProfilePath(string sid);
    void RenameAccount(string sid, string currentUsername, string newUsername);

    /// <summary>
    /// Validates credentials via LogonUser. Returns null on success, or an error message string.
    /// Returns null without attempting validation if all logon types are denied (type-not-granted).
    /// </summary>
    string? ValidatePassword(string sid, ProtectedString password, string usernameFallback);
}