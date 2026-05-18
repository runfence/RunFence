using RunFence.Core.Models;

namespace RunFence.Account.Lifecycle;

public interface IContainerDeletionService
{
    /// <summary>
    /// Full AppContainer cleanup: revert traverse access, delete OS profile, revoke VirtualStore
    /// access (best-effort), revert grants (best-effort), clean container from database.
    /// Returns a failed result if profile deletion or traverse/grant cleanup throws - caller
    /// should preserve the entry for retry.
    /// Callers handle enforcement revert, ancestor ACL recompute, and save/refresh.
    /// </summary>
    Task<ContainerDeletionResult> DeleteContainer(AppContainerEntry entry, string? containerSid);
}
