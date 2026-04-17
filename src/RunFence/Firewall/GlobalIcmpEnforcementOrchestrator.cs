using RunFence.Core.Models;

namespace RunFence.Firewall;

public class GlobalIcmpEnforcementOrchestrator(
    FirewallResolvedDomainCache domainCache,
    FirewallEnforcementRetryState retryState,
    FirewallAddressExclusionBuilder addressBuilder,
    IGlobalIcmpPolicyService globalIcmpPolicyService,
    IFirewallDomainRefreshRequester refreshRequester)
    : IGlobalIcmpSettingsApplier, IGlobalIcmpPendingDomainProcessor, IGlobalIcmpEnforcementTrigger
{
    public void ApplyGlobalIcmpSetting(AppDatabase database)
    {
        domainCache.Prune(database);
        retryState.Prune(database);

        var globalSnapshot = domainCache.GetGlobalSnapshot();
        var blockedAccounts = database.Accounts
            .Where(account => !account.Firewall.AllowInternet)
            .ToList();
        var commonExclusions = addressBuilder.ComputeCommonIcmpExclusions(
            blockedAccounts,
            globalSnapshot);

        ProcessPendingDomains(commonExclusions.PendingDomains);

        try
        {
            globalIcmpPolicyService.EnforceGlobalIcmpBlock(database, globalSnapshot);
            retryState.MarkGlobalIcmpSucceeded();
        }
        catch
        {
            retryState.MarkGlobalIcmpDirty();
            throw;
        }
    }

    public void ProcessPendingDomains(IReadOnlyList<FirewallPendingDomainResolution> pendingDomains)
    {
        MarkPendingDomainsDirty(pendingDomains);
        RequestRefreshIfNeeded(pendingDomains);
    }

    private void MarkPendingDomainsDirty(IReadOnlyList<FirewallPendingDomainResolution> pendingDomains)
    {
        foreach (var group in pendingDomains.GroupBy(pending => pending.Sid, StringComparer.OrdinalIgnoreCase))
            domainCache.MarkDirty(group.Key, group.Select(pending => pending.Domain));
    }

    private void RequestRefreshIfNeeded(IReadOnlyList<FirewallPendingDomainResolution> pendingDomains)
    {
        if (pendingDomains.Count > 0)
            refreshRequester.RequestRefresh();
    }

    public void EnforceGlobalIcmpBlock(AppDatabase database)
    {
        try
        {
            globalIcmpPolicyService.EnforceGlobalIcmpBlock(database, domainCache.GetGlobalSnapshot());
            retryState.MarkGlobalIcmpSucceeded();
        }
        catch
        {
            retryState.MarkGlobalIcmpDirty();
            refreshRequester.RequestRefresh();
            throw;
        }
    }

    public async Task EnforceGlobalIcmpBlockAsync(
        GlobalIcmpPolicyInput input,
        CancellationToken cancellationToken)
    {
        var globalResolvedDomains = domainCache.GetGlobalSnapshot();
        try
        {
            await Task.Run(
                () =>
                {
                    var globalIcmpPlan = globalIcmpPolicyService.CreateGlobalIcmpPlan(input, globalResolvedDomains);
                    globalIcmpPolicyService.EnforceGlobalIcmpBlock(globalIcmpPlan);
                },
                cancellationToken);
            retryState.MarkGlobalIcmpSucceeded();
        }
        catch
        {
            retryState.MarkGlobalIcmpDirty();
            refreshRequester.RequestRefresh();
            throw;
        }
    }
}
