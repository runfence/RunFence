using RunFence.Core.Models;

namespace RunFence.Firewall;

public class FirewallAccountRuleApplier(
    FirewallComRuleApplier comApplier,
    FirewallWfpRuleApplier wfpApplier)
    : IFirewallDnsRefreshTarget
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
            capturedRules = comApplier.GetExistingRulesBySid(sid);
            var pendingDomains = new List<FirewallPendingDomainResolution>();
            var pendingKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Internet rules (COM + WFP ICMP)
            FirewallPendingDomainHelper.AddUnique(pendingDomains, pendingKeys,
                comApplier.ApplyInternetRules(sid, username, settings, capturedRules, resolvedDomainsCache));
            wfpApplier.ApplyIcmpRules(sid, !settings.AllowInternet);

            // Localhost rules (WFP localhost + COM local address + remove legacy COM localhost rules)
            wfpApplier.ApplyLocalhostRules(sid, settings);
            comApplier.RemoveLocalhostLegacyRules(username, capturedRules);
            comApplier.ApplyLocalAddressRules(sid, username, settings, capturedRules);

            // LAN rules (COM)
            FirewallPendingDomainHelper.AddUnique(pendingDomains, pendingKeys,
                comApplier.ApplyLanRules(sid, username, settings, capturedRules, resolvedDomainsCache));

            return new FirewallAccountRuleApplyResult(true, pendingDomains);
        }
        catch (Exception ex)
        {
            if (capturedRules != null)
            {
                var rollbackSettings = previousSettings ?? new FirewallAccountSettings();
                comApplier.RollBackAccountRules(sid, rollbackSettings, capturedRules);
                wfpApplier.RollBackWfpRules(sid, rollbackSettings);
            }

            throw new FirewallApplyException(FirewallApplyPhase.AccountRules, sid, ex);
        }
    }

    public bool RefreshAllowlistRules(
        string sid,
        string username,
        FirewallAccountSettings settings,
        IReadOnlyDictionary<string, IReadOnlyList<string>> resolvedDomainsCache)
        => comApplier.RefreshAllowlistRules(sid, username, settings, resolvedDomainsCache);

    public bool RefreshLocalAddressRules(string sid, string username, FirewallAccountSettings settings)
        => comApplier.RefreshLocalAddressRules(sid, username, settings);
}
