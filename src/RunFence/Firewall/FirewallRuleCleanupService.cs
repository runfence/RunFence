using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Firewall.Wfp;

namespace RunFence.Firewall;

public class FirewallRuleCleanupService(
    ILoggingService log,
    IFirewallRuleManager ruleManager,
    IWfpLocalhostBlocker wfpBlocker,
    IWfpIcmpBlocker wfpIcmpBlocker,
    IGlobalIcmpPolicyService globalIcmpPolicyService,
    FirewallResolvedDomainCache domainCache,
    FirewallEnforcementRetryState retryState)
    : IFirewallCleanupService
{
    public void RemoveAllRules(string sid)
    {
        foreach (var rule in GetRulesBySidBestEffort(sid))
            RemoveRuleBestEffort(rule, $"FirewallRuleCleanupService: Failed to remove rule '{rule.Name}'");

        CleanupWfpForSid(sid, "FirewallRuleCleanupService: Failed to remove WFP filters");
    }

    public void RemoveAll(AppDatabase database)
    {
        log.Info("FirewallRuleCleanupService: removing all firewall rules.");

        var allRules = GetAllRulesBestEffort();
        var sidsToClean = allRules
            .Select(rule => FirewallSddlHelper.ExtractSid(rule.LocalUser))
            .Where(sid => sid != null)
            .Cast<string>()
            .Concat(database.SidNames.Keys);

        foreach (var rule in allRules)
            RemoveRuleBestEffort(rule, $"FirewallRuleCleanupService: Failed to remove rule '{rule.Name}'");

        foreach (var sid in sidsToClean.DistinctCaseInsensitive(skipWhitespace: false))
            CleanupWfpForSid(sid, "FirewallRuleCleanupService: Failed to remove WFP filters");

        try
        {
            globalIcmpPolicyService.RemoveGlobalIcmpBlock();
        }
        catch (Exception ex)
        {
            log.Error("FirewallRuleCleanupService: Failed to remove global ICMP block", ex);
        }

        domainCache.Clear();
        retryState.Clear();
    }

    public void CleanupOrphanedRules(IReadOnlySet<string> activeSids, IEnumerable<string> allKnownSids)
    {
        var activeSidSet = new HashSet<string>(activeSids, StringComparer.OrdinalIgnoreCase);
        var cleanedWfpSids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rule in GetAllRulesBestEffort())
        {
            var ruleSid = FirewallSddlHelper.ExtractSid(rule.LocalUser);
            if (ruleSid == null || activeSidSet.Contains(ruleSid))
                continue;

            RemoveRuleBestEffort(rule, $"FirewallRuleCleanupService: Failed to remove orphaned rule '{rule.Name}'");
            if (cleanedWfpSids.Add(ruleSid))
                CleanupWfpForSid(ruleSid, "FirewallRuleCleanupService: Failed to remove orphaned WFP filters");
        }

        foreach (var sid in allKnownSids.DistinctCaseInsensitive(skipWhitespace: false))
        {
            if (activeSidSet.Contains(sid) || !cleanedWfpSids.Add(sid))
                continue;

            CleanupWfpForSid(sid, "FirewallRuleCleanupService: Failed to remove inactive WFP filters");
        }
    }

    private IReadOnlyList<FirewallRuleInfo> GetRulesBySidBestEffort(string sid)
    {
        try
        {
            return ruleManager
                .GetRulesByGroup(FirewallConstants.RuleGrouping)
                .Where(r => string.Equals(FirewallSddlHelper.ExtractSid(r.LocalUser), sid, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
        catch (Exception ex)
        {
            log.Error($"FirewallRuleCleanupService: Failed to enumerate firewall rules for SID '{sid}'", ex);
            return [];
        }
    }

    private IReadOnlyList<FirewallRuleInfo> GetAllRulesBestEffort()
    {
        try
        {
            return ruleManager.GetRulesByGroup(FirewallConstants.RuleGrouping);
        }
        catch (Exception ex)
        {
            log.Error("FirewallRuleCleanupService: Failed to enumerate RunFence firewall rules", ex);
            return [];
        }
    }

    private void RemoveRuleBestEffort(FirewallRuleInfo rule, string failureMessage)
    {
        try
        {
            ruleManager.RemoveRule(rule.Name);
        }
        catch (Exception ex)
        {
            log.Error(failureMessage, ex);
        }
    }

    private void CleanupWfpForSid(string sid, string failurePrefix)
    {
        try
        {
            wfpBlocker.Apply(sid, block: false, []);
        }
        catch (Exception ex)
        {
            log.Error($"{failurePrefix}: localhost SID '{sid}'", ex);
        }

        try
        {
            wfpIcmpBlocker.Apply(sid, block: false);
        }
        catch (Exception ex)
        {
            log.Error($"{failurePrefix}: ICMP SID '{sid}'", ex);
        }
    }
}
