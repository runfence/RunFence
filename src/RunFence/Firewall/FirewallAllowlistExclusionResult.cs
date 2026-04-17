namespace RunFence.Firewall;

public sealed record FirewallAllowlistExclusionResult(
    IReadOnlyList<string> Exclusions,
    IReadOnlyList<FirewallPendingDomainResolution> PendingDomains);
