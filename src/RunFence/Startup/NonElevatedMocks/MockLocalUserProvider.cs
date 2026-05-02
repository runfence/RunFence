using RunFence.Core.Models;
using RunFence.Infrastructure;

namespace RunFence.Startup.NonElevatedMocks;

public sealed class MockLocalUserProvider(ILocalUserProvider real, NonElevatedMockStore store) : ILocalUserProvider
{
    // Read operations combine real Windows accounts with in-memory mock state.
    // Fake accounts created via MockWindowsAccountService appear here after creation.

    public IReadOnlyList<LocalUserAccount> GetLocalUserAccounts()
        => [..real.GetLocalUserAccounts(), ..store.GetAllFakeUsers()];

    public void InvalidateCache() => real.InvalidateCache();
}
