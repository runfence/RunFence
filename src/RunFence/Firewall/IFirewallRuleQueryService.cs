namespace RunFence.Firewall;

public interface IFirewallRuleQueryService
{
    IReadOnlyList<FirewallRuleInfo> GetExistingRulesBySid(string sid);
}
