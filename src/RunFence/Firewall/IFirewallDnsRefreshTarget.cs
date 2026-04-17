using RunFence.Core.Models;

namespace RunFence.Firewall;

public interface IFirewallDnsRefreshTarget
{
    bool RefreshAllowlistRules(
        string sid,
        string username,
        FirewallAccountSettings settings,
        IReadOnlyDictionary<string, IReadOnlyList<string>> resolvedDomainsCache);

    bool RefreshLocalAddressRules(string sid, string username, FirewallAccountSettings settings);
}
