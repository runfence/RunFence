using RunFence.Core.Models;

namespace RunFence.Firewall;

public interface IFirewallWfpRuleApplier
{
    void ApplyLocalhostRules(string sid, FirewallAccountSettings settings);

    void ApplyIcmpRules(string sid, bool blockInternet);

    void RollBackWfpRules(string sid, FirewallAccountSettings rollbackSettings);
}
