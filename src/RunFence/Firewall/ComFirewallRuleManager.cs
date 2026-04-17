using System.Runtime.InteropServices;
using RunFence.Core;

namespace RunFence.Firewall;

public class ComFirewallRuleManager(ILoggingService log) : IFirewallRuleManager
{
    // Creating HNetCfg.FwPolicy2 on each call is intentional: the COM object is lightweight
    // and stateless from the caller's perspective. Caching it across calls risks stale state
    // (e.g. rule collections out of sync after external changes made by Windows or other tools).
    private dynamic GetPolicy()
    {
        var policyType = Type.GetTypeFromProgID("HNetCfg.FwPolicy2")
                         ?? throw new InvalidOperationException("Windows Firewall COM class not found.");
        return Activator.CreateInstance(policyType)!;
    }

    public IReadOnlyList<FirewallRuleInfo> GetRulesByGroup(string grouping)
    {
        var policy = GetPolicy();
        var results = new List<FirewallRuleInfo>();
        foreach (dynamic rule in policy.Rules)
        {
            try
            {
                string ruleGrouping = rule.Grouping ?? "";
                if (!string.Equals(ruleGrouping, grouping, StringComparison.OrdinalIgnoreCase))
                    continue;
                results.Add(RuleToInfo(rule));
            }
            catch (COMException)
            {
            }
        }

        return results;
    }

    public void AddRule(FirewallRuleInfo rule)
    {
        var ruleType = Type.GetTypeFromProgID("HNetCfg.FWRule")
                       ?? throw new InvalidOperationException("Windows Firewall rule COM class not found.");
        dynamic comRule = Activator.CreateInstance(ruleType)!;
        ApplyInfo(comRule, rule);
        var policy = GetPolicy();
        policy.Rules.Add(comRule);
    }

    public void UpdateRule(string existingName, FirewallRuleInfo rule)
    {
        var policy = GetPolicy();
        foreach (dynamic comRule in policy.Rules)
        {
            try
            {
                if (string.Equals((string)comRule.Name, existingName, StringComparison.OrdinalIgnoreCase))
                {
                    ApplyInfo(comRule, rule);
                    return;
                }
            }
            catch (COMException)
            {
            }
        }

        log.Debug($"Rule '{existingName}' not found for update");
    }

    public void RemoveRule(string name)
    {
        var policy = GetPolicy();
        policy.Rules.Remove(name);
    }

    private static FirewallRuleInfo RuleToInfo(dynamic rule) =>
        new(
            Name: (string)(rule.Name ?? ""),
            LocalUser: (string)(rule.LocalUserAuthorizedList ?? ""),
            RemoteAddress: (string)(rule.RemoteAddresses ?? ""),
            Direction: (int)rule.Direction,
            Action: (int)rule.Action,
            Protocol: (int)rule.Protocol,
            Grouping: (string)(rule.Grouping ?? ""),
            Description: (string)(rule.Description ?? ""));

    private static void ApplyInfo(dynamic comRule, FirewallRuleInfo rule)
    {
        comRule.Name = rule.Name;
        comRule.LocalUserAuthorizedList = rule.LocalUser;
        comRule.RemoteAddresses = rule.RemoteAddress;
        comRule.Direction = rule.Direction;
        comRule.Action = rule.Action;
        comRule.Protocol = rule.Protocol;
        comRule.Grouping = rule.Grouping;
        comRule.Description = rule.Description;
        comRule.Enabled = true;
    }
}