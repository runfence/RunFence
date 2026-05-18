using RunFence.Firewall;

namespace RunFence.Account;

public interface IAccountToggleService
{
    /// <summary>
    /// Sets the logon-blocked state for an account. Performs license check, calls
    /// the restriction service, and updates grant/traverse entries in the database.
    /// </summary>
    SetLogonBlockedResult SetLogonBlocked(string sid, string username, bool blocked);

    void RestoreLogonState(string sid, string username, bool groupPolicyBlocked, bool hiddenBlocked);

    /// <summary>
    /// Updates the allow-internet firewall setting for an account and applies firewall rules.
    /// Returns the user-visible message, if any, plus whether the caller should refresh data.
    /// </summary>
    SetAllowInternetResult SetAllowInternet(string sid, bool allowInternet);
}

public record SetLogonBlockedResult(
    bool Success,
    string? ErrorMessage,
    bool IsLicenseLimit = false,
    AccountRestrictionStatus FailureStatus = AccountRestrictionStatus.Failed,
    bool RollbackAttempted = false);
