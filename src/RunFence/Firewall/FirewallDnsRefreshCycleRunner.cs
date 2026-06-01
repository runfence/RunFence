using RunFence.Core.Models;
using RunFence.Core;
using RunFence.Infrastructure;

namespace RunFence.Firewall;

public class FirewallDnsRefreshCycleRunner(
    IFirewallDnsRefreshTarget refreshTarget,
    IGlobalIcmpPolicyService globalIcmpPolicyService,
    FirewallResolvedDomainCache domainCache,
    FirewallEnforcementRetryState retryState,
    ILoggingService log,
    FirewallDomainBatchResolver batchResolver)
{
    public void RunCycle(AppDatabase database)
    {
        domainCache.Prune(database);
        retryState.Prune(database);

        var allowlistItems = SnapshotAllowlistRefreshItems(database);
        bool dnsServersChanged = UpdateDnsServerState(allowlistItems);
        var pendingDnsServerSids = retryState.GetDnsServerRefreshPendingSids();
        bool globalIcmpDirty = retryState.IsGlobalIcmpDirty();

        RefreshLocalAddressItems(SnapshotLocalAddressItems(database));

        var refreshResult = RefreshAllowlistItems(database, allowlistItems, pendingDnsServerSids, dnsServersChanged);
        if (refreshResult.AccountRefreshSucceeded
            || dnsServersChanged
            || refreshResult.DirtyStateConsumed
            || globalIcmpDirty)
        {
            EnforceGlobalIcmp(database);
        }
    }

    private bool UpdateDnsServerState(IReadOnlyList<AllowlistRefreshItem> allowlistItems)
    {
        var currentServers = batchResolver.GetDnsServerAddresses();
        bool dnsServersChanged = retryState.UpdateDnsServersAndReturnChanged(currentServers);
        if (!dnsServersChanged)
            return false;

        retryState.MarkDnsServerRefreshPending(allowlistItems.Select(item => item.Sid));
        log.Info("FirewallDnsRefreshCycleRunner: DNS server change detected.");
        return true;
    }

    private RefreshCycleResult RefreshAllowlistItems(
        AppDatabase database,
        IReadOnlyList<AllowlistRefreshItem> items,
        IReadOnlySet<string> pendingDnsServerSids,
        bool dnsServersChanged)
    {
        bool accountRefreshSucceeded = false;
        bool dirtyStateConsumed = false;
        var distinctDomainEntries = GetDistinctDomainEntries(items);
        var requestedDomains = distinctDomainEntries
            .Select(entry => entry.Value)
            .ToList();
        var freshResolved = batchResolver.ResolveBatch(distinctDomainEntries);
        var changedDomains = domainCache.UpdateResolvedDomainsAndGetChangedDomains(requestedDomains, freshResolved);
        var changedDomainSet = changedDomains.ToHashSet(StringComparer.OrdinalIgnoreCase);
        domainCache.MarkDirtyForChangedDomains(database, changedDomains);

        foreach (var item in items)
        {
            var decision = item.DomainEntries.Count > 0
                ? domainCache.GetRefreshDecision(item.Sid, item.Settings, changedDomainSet)
                : null;
            bool hasPendingDnsServerRefresh = pendingDnsServerSids.Contains(item.Sid);
            bool shouldRefresh = decision?.ShouldRefreshRules == true
                || dnsServersChanged
                || hasPendingDnsServerRefresh;

            if (!shouldRefresh)
                continue;

            try
            {
                var resolvedDomains = decision?.ResolvedDomains ?? domainCache.GetAccountSnapshot(item.Settings);
                bool changed = refreshTarget.RefreshAllowlistRules(
                    item.Sid,
                    item.Username,
                    item.Settings,
                    resolvedDomains);

                accountRefreshSucceeded = true;
                if (decision?.DomainsToClearOnSuccess.Count > 0)
                {
                    domainCache.MarkRefreshSucceeded(item.Sid, decision.DomainsToClearOnSuccess);
                    dirtyStateConsumed |= decision.WasDirty;
                }

                if (hasPendingDnsServerRefresh)
                    retryState.MarkDnsServerRefreshSucceeded(item.Sid);

                if (changed)
                    log.Info($"FirewallDnsRefreshCycleRunner: Refreshed allowlist rules for {item.Sid}");
            }
            catch (Exception ex)
            {
                log.Error($"FirewallDnsRefreshCycleRunner: Failed to refresh allowlist rules for {item.Sid}", ex);
            }
        }

        return new RefreshCycleResult(accountRefreshSucceeded, dirtyStateConsumed);
    }

    private void RefreshLocalAddressItems(IReadOnlyList<LocalAddressRefreshItem> items)
    {
        foreach (var item in items)
        {
            try
            {
                if (refreshTarget.RefreshLocalAddressRules(item.Sid, item.Username, item.Settings))
                    log.Info($"FirewallDnsRefreshCycleRunner: Refreshed local address rules for {item.Sid} due to interface change");
            }
            catch (Exception ex)
            {
                log.Error($"FirewallDnsRefreshCycleRunner: Failed to refresh local address rules for {item.Sid}", ex);
            }
        }
    }

    private void EnforceGlobalIcmp(AppDatabase database)
    {
        try
        {
            globalIcmpPolicyService.EnforceGlobalIcmpBlock(database, domainCache.GetGlobalSnapshot());
            retryState.MarkGlobalIcmpSucceeded();
        }
        catch (Exception ex)
        {
            retryState.MarkGlobalIcmpDirty();
            log.Error("FirewallDnsRefreshCycleRunner: Failed to enforce global ICMP block", ex);
        }
    }

    private static List<AllowlistRefreshItem> SnapshotAllowlistRefreshItems(AppDatabase database)
    {
        var items = new List<AllowlistRefreshItem>();
        foreach (var account in database.Accounts)
        {
            var settings = account.Firewall;
            if (settings.IsDefault || settings is { AllowInternet: true, AllowLan: true })
                continue;
            if (settings.Allowlist.Count == 0)
                continue;

            var username = database.SidNames.TryGetValue(account.Sid, out var name) ? name : account.Sid;
            var domainEntries = settings.Allowlist
                .Where(entry => entry.IsDomain)
                .ToList();
            items.Add(new AllowlistRefreshItem(account.Sid, username, settings, domainEntries));
        }

        return items;
    }

    private static List<LocalAddressRefreshItem> SnapshotLocalAddressItems(AppDatabase database)
    {
        var items = new List<LocalAddressRefreshItem>();
        foreach (var account in database.Accounts)
        {
            if (account.Firewall.IsDefault || account.Firewall.AllowLocalhost)
                continue;

            var username = database.SidNames.TryGetValue(account.Sid, out var name) ? name : account.Sid;
            items.Add(new LocalAddressRefreshItem(account.Sid, username, account.Firewall));
        }

        return items;
    }

    private static List<FirewallAllowlistEntry> GetDistinctDomainEntries(IReadOnlyList<AllowlistRefreshItem> items)
    {
        var entries = new List<FirewallAllowlistEntry>();
        var seenDomains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            foreach (var entry in item.DomainEntries)
            {
                if (!string.IsNullOrWhiteSpace(entry.Value) && seenDomains.Add(entry.Value))
                    entries.Add(entry);
            }
        }

        return entries;
    }

    private record LocalAddressRefreshItem(string Sid, string Username, FirewallAccountSettings Settings);
    private record RefreshCycleResult(bool AccountRefreshSucceeded, bool DirtyStateConsumed);
}
