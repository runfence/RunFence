namespace RunFence.Firewall;

public sealed record GlobalIcmpEnforcementPlan(
    bool Enabled,
    int BlockedAccountCount,
    IReadOnlyList<string> CommonExclusions);
