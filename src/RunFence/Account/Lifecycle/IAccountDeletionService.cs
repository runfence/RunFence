using RunFence.Core.Models;

namespace RunFence.Account.Lifecycle;

public interface IAccountDeletionService
{
    /// <summary>
    /// Full account cleanup: clear restrictions, remove firewall rules, delete Windows account,
    /// remove credentials, revert grants, revert/recompute app ACLs, clean SID from database.
    /// Does NOT delete profile — caller handles that separately.
    /// Throws on DeleteUser failure. Callers handle save/refresh/cache invalidation.
    /// </summary>
    void DeleteAccount(string sid, string username,
        CredentialStore credentialStore,
        bool removeApps = true);
}