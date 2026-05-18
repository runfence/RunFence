namespace RunFence.Firewall;

public sealed record FirewallEnforcementEntry(
    FirewallEnforcementLayer Layer,
    FirewallEnforcementStatus Status,
    string? Error = null);
