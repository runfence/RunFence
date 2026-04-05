namespace RunFence.Firewall;

public record FirewallRuleInfo(
    string Name,
    string LocalUser,
    string RemoteAddress,
    int Direction,
    int Action,
    int Protocol,
    string Grouping,
    string Description);

public interface IFirewallRuleManager
{
    IReadOnlyList<FirewallRuleInfo> GetRulesByGroup(string grouping);
    void AddRule(FirewallRuleInfo rule);
    void UpdateRule(string existingName, FirewallRuleInfo rule);
    void RemoveRule(string name);
}