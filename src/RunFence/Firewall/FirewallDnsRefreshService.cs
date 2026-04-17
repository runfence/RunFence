using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using Timer = System.Threading.Timer;

namespace RunFence.Firewall;

/// <summary>
/// Periodically re-resolves domain allowlist entries and refreshes firewall rules when
/// DNS, local interface, or retry state says existing rules may be stale.
/// </summary>
public class FirewallDnsRefreshService(
    IFirewallDnsRefreshTarget refreshTarget,
    IGlobalIcmpPolicyService globalIcmpPolicyService,
    FirewallResolvedDomainCache domainCache,
    FirewallEnforcementRetryState retryState,
    ILoggingService log,
    UiThreadDatabaseAccessor db,
    IFirewallNetworkInfo firewallNetworkInfo)
    : IDisposable, IBackgroundService, IFirewallDomainRefreshRequester
{
    private Timer? _timer;
    private int _isRefreshWorkerRunning;
    private int _refreshRequested;
    private volatile bool _disposed;

    public void Start()
    {
        log.Info("FirewallDnsRefreshService: starting DNS refresh timer in background.");

        Task.Run(() =>
        {
            InitializeDnsState();
            if (_disposed)
                return;

            var timer = new Timer(
                OnTimerTick,
                null,
                TimeSpan.FromSeconds(60),
                TimeSpan.FromSeconds(60));
            _timer = timer;

            if (_disposed)
            {
                _timer = null;
                timer.Dispose();
            }
            else
            {
                log.Info("FirewallDnsRefreshService: timer started.");
            }
        });
    }

    public void RequestRefresh()
    {
        if (_disposed)
            return;

        Volatile.Write(ref _refreshRequested, 1);
        if (TryAcquireRefreshWorker())
            _ = Task.Run(RunRefreshDrainLoop);
    }

    /// <summary>
    /// Synchronously processes one DNS refresh cycle on a background thread when idle.
    /// If a refresh worker is already active, queues a follow-up cycle and returns.
    /// </summary>
    public void ProcessDnsRefresh()
    {
        if (_disposed)
            return;

        if (!TryAcquireRefreshWorker())
        {
            Volatile.Write(ref _refreshRequested, 1);
            return;
        }

        Interlocked.Exchange(ref _refreshRequested, 0);
        try
        {
            try
            {
                var database = db.CreateSnapshot();
                StartDnsRefreshCycle(database);
            }
            catch (Exception ex)
            {
                log.Error("FirewallDnsRefreshService: DNS refresh cycle failed", ex);
            }
        }
        finally
        {
            if (ReleaseRefreshWorkerAndShouldContinue())
                _ = Task.Run(RunRefreshDrainLoop);
        }
    }

    private void OnTimerTick(object? state)
    {
        RequestRefresh();
    }

    private void RunRefreshDrainLoop()
    {
        while (true)
        {
            while (!_disposed && Interlocked.Exchange(ref _refreshRequested, 0) == 1)
            {
                try
                {
                    StartDnsRefreshCycle();
                }
                catch (Exception ex)
                {
                    log.Error("FirewallDnsRefreshService: DNS refresh cycle failed", ex);
                }
            }

            if (ReleaseRefreshWorkerAndShouldContinue())
                continue;

            return;
        }
    }

    private bool TryAcquireRefreshWorker() =>
        Interlocked.CompareExchange(ref _isRefreshWorkerRunning, 1, 0) == 0;

    private bool ReleaseRefreshWorkerAndShouldContinue()
    {
        Volatile.Write(ref _isRefreshWorkerRunning, 0);
        if (_disposed || Volatile.Read(ref _refreshRequested) == 0)
            return false;

        return TryAcquireRefreshWorker();
    }

    /// <summary>
    /// Takes a fresh snapshot, resolves domains on the worker thread, and refreshes stale rules.
    /// </summary>
    private void StartDnsRefreshCycle()
    {
        var database = db.CreateSnapshot();
        StartDnsRefreshCycle(database);
    }

    private void StartDnsRefreshCycle(AppDatabase database)
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

    private void InitializeDnsState()
    {
        try
        {
            var database = db.CreateSnapshot();
            domainCache.Prune(database);
            retryState.Prune(database);
            retryState.UpdateDnsServersAndReturnChanged(firewallNetworkInfo.GetDnsServerAddresses());
        }
        catch (Exception ex)
        {
            log.Warn($"FirewallDnsRefreshService: failed to initialize DNS refresh state: {ex.Message}");
        }
    }

    private bool UpdateDnsServerState(IReadOnlyList<AllowlistRefreshItem> allowlistItems)
    {
        var currentServers = firewallNetworkInfo.GetDnsServerAddresses();
        bool dnsServersChanged = retryState.UpdateDnsServersAndReturnChanged(currentServers);
        if (!dnsServersChanged)
            return false;

        retryState.MarkDnsServerRefreshPending(allowlistItems.Select(item => item.Sid));
        log.Info("FirewallDnsRefreshService: DNS server change detected.");
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
        var freshResolved = ResolveDistinctDomainEntries(distinctDomainEntries);
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
                    log.Info($"FirewallDnsRefreshService: Refreshed allowlist rules for {item.Sid}");
            }
            catch (Exception ex)
            {
                log.Error($"FirewallDnsRefreshService: Failed to refresh allowlist rules for {item.Sid}", ex);
            }
        }

        return new RefreshCycleResult(accountRefreshSucceeded, dirtyStateConsumed);
    }

    private Dictionary<string, List<string>> ResolveDistinctDomainEntries(IReadOnlyList<FirewallAllowlistEntry> domainEntries)
    {
        if (domainEntries.Count == 0)
            return new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        var freshResolved = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, List<string>> resolved;
        try
        {
            resolved = firewallNetworkInfo.ResolveDomainEntriesAsync(domainEntries)
                .GetAwaiter()
                .GetResult();
        }
        catch (Exception)
        {
            foreach (var entry in domainEntries)
            {
                try
                {
                    var single = firewallNetworkInfo.ResolveDomainEntriesAsync([entry])
                        .GetAwaiter().GetResult();
                    if (single.TryGetValue(entry.Value, out var addrs)
                        && addrs.Any(a => !string.IsNullOrWhiteSpace(a)))
                        freshResolved[entry.Value] = addrs.ToList();
                }
                catch (Exception perEx)
                {
                    log.Warn($"FirewallDnsRefreshService: DNS resolution failed for {entry.Value}: {perEx.Message}");
                }
            }
            return freshResolved;
        }

        foreach (var entry in domainEntries)
        {
            if (!resolved.TryGetValue(entry.Value, out var addresses)
                || !addresses.Any(address => !string.IsNullOrWhiteSpace(address)))
            {
                log.Warn($"FirewallDnsRefreshService: DNS returned no addresses for {entry.Value}");
                continue;
            }

            freshResolved[entry.Value] = addresses.ToList();
        }

        return freshResolved;
    }

    private void RefreshLocalAddressItems(IReadOnlyList<LocalAddressRefreshItem> items)
    {
        foreach (var item in items)
        {
            try
            {
                if (refreshTarget.RefreshLocalAddressRules(item.Sid, item.Username, item.Settings))
                    log.Info($"FirewallDnsRefreshService: Refreshed local address rules for {item.Sid} due to interface change");
            }
            catch (Exception ex)
            {
                log.Error($"FirewallDnsRefreshService: Failed to refresh local address rules for {item.Sid}", ex);
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
            log.Error("FirewallDnsRefreshService: Failed to enforce global ICMP block", ex);
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

    public void Dispose()
    {
        _disposed = true;
        _timer?.Dispose();
        _timer = null;
    }

    private record AllowlistRefreshItem(
        string Sid,
        string Username,
        FirewallAccountSettings Settings,
        IReadOnlyList<FirewallAllowlistEntry> DomainEntries);

    private record LocalAddressRefreshItem(string Sid, string Username, FirewallAccountSettings Settings);

    private record RefreshCycleResult(bool AccountRefreshSucceeded, bool DirtyStateConsumed);
}
