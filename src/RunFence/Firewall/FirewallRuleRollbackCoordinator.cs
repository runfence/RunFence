using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.Firewall;

public sealed class FirewallRuleRollbackCoordinator(
    IFirewallRuleQueryService ruleQueryService,
    IFirewallRuleManager ruleManager,
    ILoggingService log)
{
    public void RestoreWindowsFirewallRules(string sid, IReadOnlyList<FirewallRuleInfo> capturedRules)
    {
        IReadOnlyList<FirewallRuleInfo> currentRules;
        try
        {
            currentRules = ruleQueryService.GetExistingRulesBySid(sid);
        }
        catch (Exception ex)
        {
            log.Error($"FirewallRuleRollbackCoordinator: Failed to enumerate firewall rules for rollback of SID '{sid}'", ex);
            return;
        }

        var capturedByName = RulesByName(capturedRules);
        foreach (var currentRule in currentRules)
        {
            if (capturedByName.ContainsKey(currentRule.Name))
                continue;

            try
            {
                ruleManager.RemoveRule(currentRule.Name);
            }
            catch (Exception ex)
            {
                log.Error($"FirewallRuleRollbackCoordinator: Failed to remove newly-created rollback rule '{currentRule.Name}'", ex);
            }
        }

        IReadOnlyList<FirewallRuleInfo> postRemovalRules;
        try
        {
            postRemovalRules = ruleQueryService.GetExistingRulesBySid(sid);
        }
        catch (Exception ex)
        {
            log.Error($"FirewallRuleRollbackCoordinator: Failed to re-enumerate firewall rules for rollback of SID '{sid}'", ex);
            postRemovalRules = currentRules.Where(r => capturedByName.ContainsKey(r.Name)).ToList();
        }

        var currentByName = RulesByName(postRemovalRules);
        foreach (var capturedRule in capturedRules)
        {
            if (!currentByName.TryGetValue(capturedRule.Name, out var currentRule))
            {
                try
                {
                    ruleManager.AddRule(capturedRule);
                }
                catch (Exception ex)
                {
                    log.Error($"FirewallRuleRollbackCoordinator: Failed to restore missing rollback rule '{capturedRule.Name}'", ex);
                }

                continue;
            }

            if (currentRule == capturedRule)
                continue;

            try
            {
                ruleManager.UpdateRule(currentRule.Name, capturedRule);
            }
            catch (Exception ex)
            {
                log.Error($"FirewallRuleRollbackCoordinator: Failed to restore rollback rule '{capturedRule.Name}'", ex);
            }
        }
    }

    private static Dictionary<string, FirewallRuleInfo> RulesByName(IReadOnlyList<FirewallRuleInfo> rules)
    {
        var byName = new Dictionary<string, FirewallRuleInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in rules)
            byName.TryAdd(rule.Name, rule);
        return byName;
    }
}
