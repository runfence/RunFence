namespace RunFence.Firewall;

public sealed record FirewallAddressComputationResult(
    string IPv4Address,
    string IPv6Address,
    IReadOnlyList<FirewallPendingDomainResolution> PendingDomains);
