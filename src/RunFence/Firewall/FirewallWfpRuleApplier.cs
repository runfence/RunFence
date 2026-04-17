using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Firewall.Wfp;

namespace RunFence.Firewall;

/// <summary>
/// Manages WFP-based localhost and ICMP firewall filters.
/// </summary>
public class FirewallWfpRuleApplier(
    IWfpLocalhostBlocker wfpBlocker,
    IWfpIcmpBlocker wfpIcmpBlocker,
    ILoggingService log)
{
    public void ApplyLocalhostRules(string sid, FirewallAccountSettings settings)
    {
        wfpBlocker.Apply(sid, !settings.AllowLocalhost, settings.LocalhostPortExemptions);
    }

    public void ApplyIcmpRules(string sid, bool blockInternet)
    {
        wfpIcmpBlocker.Apply(sid, blockInternet);
    }

    public void RollBackWfpRules(string sid, FirewallAccountSettings rollbackSettings)
    {
        try
        {
            wfpBlocker.Apply(sid, block: !rollbackSettings.AllowLocalhost, rollbackSettings.LocalhostPortExemptions);
        }
        catch (Exception ex)
        {
            log.Error($"FirewallWfpRuleApplier: Failed to roll back WFP localhost filters for SID '{sid}'", ex);
        }

        try
        {
            wfpIcmpBlocker.Apply(sid, block: !rollbackSettings.AllowInternet);
        }
        catch (Exception ex)
        {
            log.Error($"FirewallWfpRuleApplier: Failed to roll back WFP ICMP filters for SID '{sid}'", ex);
        }
    }
}
