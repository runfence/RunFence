namespace RunFence.Firewall;

public sealed record FirewallAccountRuleApplyResult(
    bool AccountRulesApplied,
    IReadOnlyList<FirewallPendingDomainResolution> PendingDomains);
