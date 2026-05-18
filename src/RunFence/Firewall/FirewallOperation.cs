using RunFence.Core.Models;

namespace RunFence.Firewall;

public sealed record FirewallOperation(
    FirewallEnforcementLayer Layer,
    string AccountSid,
    FirewallAccountSettings PreviousSettings,
    FirewallAccountSettings RequestedSettings);
