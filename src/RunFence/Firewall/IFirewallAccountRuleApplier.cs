using RunFence.Core.Models;

namespace RunFence.Firewall;

public interface IFirewallAccountRuleApplier : IFirewallDnsRefreshTarget
{
    FirewallAccountRuleApplyResult ApplyFirewallRules(
        string sid,
        string username,
        FirewallAccountSettings settings,
        FirewallAccountSettings? previousSettings,
        IReadOnlyDictionary<string, IReadOnlyList<string>> resolvedDomainsCache);
}
