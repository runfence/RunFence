using RunFence.Core.Models;

namespace RunFence.Account.Lifecycle;

public interface IAccountDeletionService
{
    /// <summary>
    /// Full account cleanup: clear restrictions, remove firewall rules, delete Windows account,
    /// remove credentials, revert grants, revert/recompute app ACLs, clean SID from database,
    /// and then attempt profile deletion as warning-only post-SAM cleanup.
    /// Throws on DeleteUser failure. Callers handle save/refresh/cache invalidation.
    /// </summary>
    Task<AccountDeletionCleanupResult> DeleteAccountAsync(string sid, string username,
        CredentialStore credentialStore,
        bool removeApps = true);
}
