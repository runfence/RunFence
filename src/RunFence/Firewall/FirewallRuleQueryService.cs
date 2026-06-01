namespace RunFence.Firewall;

public sealed class FirewallRuleQueryService(IFirewallRuleManager ruleManager) : IFirewallRuleQueryService
{
    public IReadOnlyList<FirewallRuleInfo> GetExistingRulesBySid(string sid)
    {
        var allRules = ruleManager.GetRulesByGroup(FirewallConstants.RuleGrouping);
        return allRules
            .Where(r => string.Equals(FirewallSddlHelper.ExtractSid(r.LocalUser), sid, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }
}
