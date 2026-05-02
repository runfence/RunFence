using RunFence.Core.Models;

namespace RunFence.Firewall;

public record AllowlistRefreshItem(
    string Sid,
    string Username,
    FirewallAccountSettings Settings,
    IReadOnlyList<FirewallAllowlistEntry> DomainEntries);
