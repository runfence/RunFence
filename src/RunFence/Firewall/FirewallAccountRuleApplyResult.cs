namespace RunFence.Firewall;

public sealed record FirewallAccountRuleApplyResult(
    bool Succeeded,
    IReadOnlyList<FirewallPendingDomainResolution> PendingDomains,
    FirewallEnforcementLayer? FailedLayer = null,
    string? ErrorMessage = null);
