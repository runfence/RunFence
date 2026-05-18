using RunFence.Account.OrphanedProfiles;
using RunFence.Core.Models;

namespace RunFence.Account;

public interface IAccountLifecycleManager
{
    Task<AccountDeleteValidationResult> ValidateDeleteAsync(string sid);
    void ClearAccountRestrictions(string sid, string username, AppSettings? settings = null);
    AccountDeletionResult DeleteSamAccount(string sid);
    Task<string?> DeleteProfileAsync(string sid);
    Task<AclReferenceCleanupResult> CleanupAclReferencesAsync(
        List<string> sids, IProgress<AclCleanupProgress>? progress, CancellationToken ct);
}
