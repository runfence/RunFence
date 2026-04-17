namespace RunFence.Account;

public interface IWindowsAccountService
{
    void DeleteUser(string sid);
    string CreateLocalUser(string username, string password);
    string? GetProfilePath(string sid);
    void RenameAccount(string sid, string currentUsername, string newUsername);

    /// <summary>
    /// Validates credentials via LogonUser. Returns null on success, or an error message string.
    /// Returns null without attempting validation if all logon types are denied (type-not-granted).
    /// </summary>
    string? ValidatePassword(string sid, string password, string usernameFallback);
}