using RunFence.Core.Models;

namespace RunFence.Firewall;

public class FirewallAccountRuleApplier(
    IFirewallRuleQueryService ruleQueryService,
    FirewallRulePairSynchronizer ruleSynchronizer,
    FirewallRuleRollbackCoordinator rollbackCoordinator,
    IFirewallWfpRuleApplier wfpApplier)
    : IFirewallAccountRuleApplier
{
    public FirewallAccountRuleApplyResult ApplyFirewallRules(
        string sid,
        string username,
        FirewallAccountSettings settings,
        FirewallAccountSettings? previousSettings,
        IReadOnlyDictionary<string, IReadOnlyList<string>> resolvedDomainsCache)
    {
        IReadOnlyList<FirewallRuleInfo>? capturedRules = null;
        try
        {
            capturedRules = ruleQueryService.GetExistingRulesBySid(sid);
            var pendingDomains = new List<FirewallPendingDomainResolution>();
            var pendingKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Internet rules (COM + WFP ICMP)
            FirewallPendingDomainHelper.AddUnique(pendingDomains, pendingKeys,
                ruleSynchronizer.ApplyInternetRules(sid, username, settings, capturedRules, resolvedDomainsCache));
            wfpApplier.ApplyIcmpRules(sid, !settings.AllowInternet);

            // Localhost rules (WFP localhost + COM local address + remove legacy COM localhost rules)
            wfpApplier.ApplyLocalhostRules(sid, settings);
            ruleSynchronizer.RemoveLocalhostLegacyRules(username, capturedRules);
            ruleSynchronizer.ApplyLocalAddressRules(sid, username, settings, capturedRules);

            // LAN rules (COM)
            FirewallPendingDomainHelper.AddUnique(pendingDomains, pendingKeys,
                ruleSynchronizer.ApplyLanRules(sid, username, settings, capturedRules, resolvedDomainsCache));

            return new FirewallAccountRuleApplyResult(true, pendingDomains);
        }
        catch (Exception ex)
        {
            if (capturedRules != null)
            {
                var rollbackSettings = previousSettings ?? new FirewallAccountSettings();
                rollbackCoordinator.RestoreWindowsFirewallRules(sid, capturedRules);
                wfpApplier.RollBackWfpRules(sid, rollbackSettings);
            }

            return new FirewallAccountRuleApplyResult(
                false,
                [],
                IsWfpFailure(ex) ? FirewallEnforcementLayer.WfpFilters : FirewallEnforcementLayer.AccountRules,
                ex.Message);
        }
    }

    public bool RefreshAllowlistRules(
        string sid,
        string username,
        FirewallAccountSettings settings,
        IReadOnlyDictionary<string, IReadOnlyList<string>> resolvedDomainsCache)
    {
        if (settings is { AllowInternet: true, AllowLan: true })
            return false;

        var existing = ruleQueryService.GetExistingRulesBySid(sid);
        return ruleSynchronizer.RefreshAllowlistRules(sid, username, settings, resolvedDomainsCache, existing);
    }

    public bool RefreshLocalAddressRules(string sid, string username, FirewallAccountSettings settings)
    {
        if (settings.AllowLocalhost)
            return false;

        var existing = ruleQueryService.GetExistingRulesBySid(sid);
        return ruleSynchronizer.RefreshLocalAddressRules(sid, username, settings, existing);
    }

    private static bool IsWfpFailure(Exception ex)
        => ex is Wfp.WfpFilterHelperException
           || ex.InnerException is Wfp.WfpFilterHelperException;
}
