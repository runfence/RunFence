using RunFence.Core.Models;

namespace RunFence.Account.Lifecycle;

public interface IContainerDeletionService
{
    /// <summary>
    /// Full AppContainer cleanup: revert traverse access, delete OS profile, revoke VirtualStore
    /// access (best-effort), revert grants (best-effort), clean container from database.
    /// Returns false if DeleteProfile fails — caller should preserve the entry for retry.
    /// Callers handle enforcement revert, ancestor ACL recompute, and save/refresh.
    /// </summary>
    bool DeleteContainer(AppContainerEntry entry, string? containerSid);
}