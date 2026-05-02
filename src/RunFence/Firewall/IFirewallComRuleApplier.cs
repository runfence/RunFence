using RunFence.Core.Models;

namespace RunFence.Firewall;

public interface IFirewallComRuleApplier
{
    IReadOnlyList<FirewallPendingDomainResolution> ApplyInternetRules(
        string sid,
        string username,
        FirewallAccountSettings settings,
        IReadOnlyList<FirewallRuleInfo> existing,
        IReadOnlyDictionary<string, IReadOnlyList<string>> resolvedDomainsCache);

    IReadOnlyList<FirewallPendingDomainResolution> ApplyLanRules(
        string sid,
        string username,
        FirewallAccountSettings settings,
        IReadOnlyList<FirewallRuleInfo> existing,
        IReadOnlyDictionary<string, IReadOnlyList<string>> resolvedDomainsCache);

    void RemoveLocalhostLegacyRules(string username, IReadOnlyList<FirewallRuleInfo> existing);

    IReadOnlyList<FirewallPendingDomainResolution> ApplyLocalAddressRules(
        string sid,
        string username,
        FirewallAccountSettings settings,
        IReadOnlyList<FirewallRuleInfo> existing);

    bool RefreshAllowlistRules(
        string sid,
        string username,
        FirewallAccountSettings settings,
        IReadOnlyDictionary<string, IReadOnlyList<string>> resolvedDomainsCache);

    bool RefreshLocalAddressRules(string sid, string username, FirewallAccountSettings settings);

    IReadOnlyList<FirewallRuleInfo> GetExistingRulesBySid(string sid);

    void RollBackAccountRules(string sid, IReadOnlyList<FirewallRuleInfo> capturedRules);
}
