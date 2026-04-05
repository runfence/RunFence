using RunFence.Core.Models;

namespace RunFence.Firewall;

public interface IFirewallService
{
    void ApplyFirewallRules(string sid, string username, FirewallAccountSettings settings,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? preResolvedDomains = null);

    void RemoveAllRules(string sid);

    bool RefreshAllowlistRules(string sid, string username, FirewallAccountSettings settings,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? preResolvedDomains = null);

    bool RefreshLocalAddressRules(string sid, string username, FirewallAccountSettings settings);
    void EnforceAll(AppDatabase database);
}