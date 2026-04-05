using RunFence.Core.Models;

namespace RunFence.Infrastructure;

/// <summary>
/// Provides the list of local Windows user accounts for ACL computation.
/// Implementations should cache results and support explicit cache invalidation.
/// </summary>
public interface ILocalUserProvider
{
    List<LocalUserAccount> GetLocalUserAccounts();
    void InvalidateCache();
}