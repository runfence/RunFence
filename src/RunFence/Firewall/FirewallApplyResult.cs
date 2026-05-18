using System.Linq;

namespace RunFence.Firewall;

public sealed record FirewallApplyResult(
    bool ConfigSaved,
    IReadOnlyList<FirewallPendingDomainResolution> PendingDomains,
    IReadOnlyList<FirewallEnforcementEntry> EnforcementEntries)
{
    public bool HasBlockingFailure =>
        EnforcementEntries.Any(entry =>
            entry.Status == FirewallEnforcementStatus.Failed
            && (entry.Layer == FirewallEnforcementLayer.AccountRules
                || entry.Layer == FirewallEnforcementLayer.WfpFilters));

    public FirewallEnforcementEntry? FirstBlockingFailure =>
        EnforcementEntries.FirstOrDefault(entry =>
            entry.Status == FirewallEnforcementStatus.Failed
            && (entry.Layer == FirewallEnforcementLayer.AccountRules
                || entry.Layer == FirewallEnforcementLayer.WfpFilters));

    public bool AccountRulesApplied =>
        EnforcementEntries.Any(entry => entry.Layer == FirewallEnforcementLayer.AccountRules && entry.Status == FirewallEnforcementStatus.Succeeded);

    public bool GlobalIcmpApplied =>
        EnforcementEntries.Any(entry => entry.Layer == FirewallEnforcementLayer.GlobalIcmp && entry.Status == FirewallEnforcementStatus.Succeeded);
}
