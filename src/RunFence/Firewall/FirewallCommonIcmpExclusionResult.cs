namespace RunFence.Firewall;

public sealed record FirewallCommonIcmpExclusionResult(
    IReadOnlyList<string> CommonExclusions,
    IReadOnlyList<FirewallPendingDomainResolution> PendingDomains);
