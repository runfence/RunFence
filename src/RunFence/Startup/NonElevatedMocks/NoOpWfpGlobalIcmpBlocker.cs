#pragma warning disable CS9113 // Parameter 'real' is intentionally unread — RegisterDecorator constructs the real implementation for DI validation
using RunFence.Firewall.Wfp;

namespace RunFence.Startup.NonElevatedMocks;

public sealed class NoOpWfpGlobalIcmpBlocker(IWfpGlobalIcmpBlocker real) : IWfpGlobalIcmpBlocker
{
    // real is injected but unused — RegisterDecorator constructs the real implementation
    // to keep DI validation working; all calls are no-ops in non-elevated debug mode

    public void Apply(IReadOnlyList<string> ipv4CidrRanges, IReadOnlyList<string> ipv6CidrRanges) { }
}
