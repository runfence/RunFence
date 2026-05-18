namespace RunFence.Firewall;

public sealed record FirewallApplyPhaseResult(
    bool CanContinue,
    bool PersistenceCompleted,
    FirewallAccountRuleApplyResult? AccountApplyResult,
    bool GlobalIcmpApplied,
    string? GlobalIcmpError,
    IReadOnlyList<FirewallEnforcementEntry> Entries);
