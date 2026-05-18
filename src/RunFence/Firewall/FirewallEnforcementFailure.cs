namespace RunFence.Firewall;

public sealed record FirewallEnforcementFailure(
    FirewallEnforcementLayer Layer,
    string? AccountSid,
    string Message);
