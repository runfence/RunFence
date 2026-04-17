using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Firewall.Wfp;

namespace RunFence.Firewall;

public class FirewallGlobalIcmpEnforcer(
    ILoggingService log,
    FirewallAddressRangeBuilder rangeBuilder,
    FirewallAddressExclusionBuilder addressBuilder,
    IWfpGlobalIcmpBlocker wfpGlobalIcmpBlocker)
    : IGlobalIcmpPolicyService
{
    public GlobalIcmpEnforcementPlan CreateGlobalIcmpPlan(
        AppDatabase database,
        IReadOnlyDictionary<string, IReadOnlyList<string>> resolvedDomainsCache)
        => CreateGlobalIcmpPlan(
            new GlobalIcmpPolicyInput(
                database.Settings.BlockIcmpWhenInternetBlocked,
                database.Accounts.Where(a => !a.Firewall.AllowInternet).ToList()),
            resolvedDomainsCache);

    public GlobalIcmpEnforcementPlan CreateGlobalIcmpPlan(
        GlobalIcmpPolicyInput input,
        IReadOnlyDictionary<string, IReadOnlyList<string>> resolvedDomainsCache)
    {
        if (!input.BlockIcmpWhenInternetBlocked)
            return new GlobalIcmpEnforcementPlan(false, 0, []);

        if (input.BlockedAccounts.Count == 0)
            return new GlobalIcmpEnforcementPlan(true, 0, []);

        var commonExclusions = addressBuilder
            .ComputeCommonIcmpExclusions(input.BlockedAccounts, resolvedDomainsCache)
            .CommonExclusions;

        return new GlobalIcmpEnforcementPlan(true, input.BlockedAccounts.Count, commonExclusions);
    }

    public void EnforceGlobalIcmpBlock(GlobalIcmpEnforcementPlan plan)
    {
        if (!plan.Enabled || plan.BlockedAccountCount == 0)
        {
            wfpGlobalIcmpBlocker.Apply([], []);
            return;
        }

        log.Info($"Global ICMP block: {plan.BlockedAccountCount} account(s) with internet blocked, " +
                 $"excluded IPs (not blocked): [{string.Join(", ", plan.CommonExclusions)}]");

        var ipv4Cidrs = SplitCidrs(rangeBuilder.BuildInternetIPv4Range(plan.CommonExclusions));
        var ipv6Cidrs = SplitCidrs(rangeBuilder.BuildInternetIPv6Range(plan.CommonExclusions));
        wfpGlobalIcmpBlocker.Apply(ipv4Cidrs, ipv6Cidrs);
    }

    public void EnforceGlobalIcmpBlock(
        AppDatabase database,
        IReadOnlyDictionary<string, IReadOnlyList<string>> resolvedDomainsCache)
        => EnforceGlobalIcmpBlock(CreateGlobalIcmpPlan(database, resolvedDomainsCache));

    public void RemoveGlobalIcmpBlock()
    {
        wfpGlobalIcmpBlocker.Apply([], []);
    }

    private static IReadOnlyList<string> SplitCidrs(string ranges)
        => string.IsNullOrEmpty(ranges) ? [] : ranges.Split(',');
}
