namespace RunFence.Account;

public interface IAccountToggleService
{
    /// <summary>
    /// Sets the logon-blocked state for an account. Performs license check, calls
    /// the restriction service, and updates grant/traverse entries in the database.
    /// </summary>
    SetLogonBlockedResult SetLogonBlocked(string sid, string username, bool blocked);

    /// <summary>
    /// Updates the allow-internet firewall setting for an account and applies firewall rules.
    /// Returns an error message if the firewall rule application fails, null on success.
    /// </summary>
    string? SetAllowInternet(string sid, string username, bool allowInternet);
}

public record SetLogonBlockedResult(bool Success, string? ErrorMessage, bool IsLicenseLimit = false);