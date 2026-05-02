#pragma warning disable CS9113 // Parameter 'real' is intentionally unread — RegisterDecorator constructs the real implementation for DI validation
using RunFence.Firewall;
using RunFence.Firewall.Wfp;

namespace RunFence.Startup.NonElevatedMocks;

public sealed class NoOpWfpLocalhostBlocker(IWfpLocalhostBlocker real) : IWfpLocalhostBlocker
{
    // real is injected but unused — RegisterDecorator constructs the real implementation
    // to keep DI validation working; all calls are no-ops in non-elevated debug mode

    public void Apply(string sid, bool block, IReadOnlyList<string> allowedPorts) { }
    public void UpdateEphemeralPorts(string sid, IReadOnlyList<PortRange> ephemeralBlockedRanges) { }
}
