#pragma warning disable CS9113 // Parameter 'real' is intentionally unread — RegisterDecorator constructs the real implementation for DI validation
using RunFence.Infrastructure;

namespace RunFence.Startup.NonElevatedMocks;

public sealed class NoOpUserHiveManager(IUserHiveManager real) : IUserHiveManager
{
    // real is injected but unused — RegisterDecorator constructs the real implementation
    // to keep DI validation working; all calls are no-ops in non-elevated debug mode

    public IDisposable? EnsureHiveLoaded(string sid) => new NoOpDisposable();
    public bool IsHiveLoaded(string sid) => true;

    private sealed class NoOpDisposable : IDisposable
    {
        public void Dispose() { }
    }
}
