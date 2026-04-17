namespace RunFence.Firewall;

public sealed record FirewallApplyResult(
    bool AccountRulesApplied,
    bool GlobalIcmpApplied,
    IReadOnlyList<FirewallPendingDomainResolution> PendingDomains);
