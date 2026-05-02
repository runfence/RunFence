#pragma warning disable CS9113 // Parameter 'real' is intentionally unread — RegisterDecorator constructs the real implementation for DI validation
using RunFence.Firewall;

namespace RunFence.Startup.NonElevatedMocks;

public sealed class NoOpBlockedConnectionReader(IBlockedConnectionReader real) : IBlockedConnectionReader
{
    // real is injected but unused — RegisterDecorator constructs the real implementation
    // to keep DI validation working; all calls are no-ops in non-elevated debug mode

    public List<BlockedConnection> ReadBlockedConnections(TimeSpan lookback) => [];
    public bool IsAuditPolicyEnabled() => false;
    public void SetAuditPolicyEnabled(bool enabled) { }
}
