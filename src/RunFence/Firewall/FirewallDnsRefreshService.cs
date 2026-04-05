using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Persistence;
using Timer = System.Threading.Timer;

namespace RunFence.Firewall;

/// <summary>
/// Periodically re-resolves domain allowlist entries and refreshes firewall rules when
/// resolved IPs change. Uses a 60-second timer. DNS resolution runs on a thread-pool
/// thread; COM firewall calls are dispatched back to the UI thread.
/// </summary>
public class FirewallDnsRefreshService(
    IFirewallService firewallService,
    ILoggingService log,
    IDatabaseProvider databaseProvider,
    IUiThreadInvoker uiThreadInvoker,
    IFirewallNetworkInfo firewallNetworkInfo)
    : IDisposable, IBackgroundService
{
    private Timer? _timer;

    // SID → (domain → resolved IPs), populated on Start() to avoid spurious updates on first tick
    private readonly Dictionary<string, Dictionary<string, List<string>>> _lastResolvedCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _cacheLock = new();
    private IReadOnlyList<string> _lastDnsServers = [];
    private bool _isRefreshInProgress;
    private volatile bool _disposed;

    public void Start()
    {
        log.Info("FirewallDnsRefreshService: starting, initializing DNS cache in background.");

        // Pre-populate cache on a background thread so startup is not blocked by DNS resolution.
        // Timer starts only after cache init completes so the first tick compares against
        // already-resolved state and avoids spurious firewall rule updates.
        var snapshot = databaseProvider.GetDatabase().CreateSnapshot();
        Task.Run(() =>
        {
            InitializeCache(snapshot);
            if (!_disposed)
            {
                var timer = new Timer(
                    OnTimerTick, null,
                    TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60));
                _timer = timer;
                // Re-check _disposed after assigning _timer to handle disposal racing with timer creation.
                if (_disposed)
                {
                    _timer = null;
                    timer.Dispose();
                }
                else
                {
                    log.Info("FirewallDnsRefreshService: DNS cache initialized, timer started.");
                }
            }
        });
    }

    private void OnTimerTick(object? state)
    {
        try
        {
            uiThreadInvoker.Invoke(StartDnsRefreshCycle);
        }
        catch (ObjectDisposedException)
        {
        }
    }

    /// <summary>
    /// Runs on the UI thread. Takes a lightweight snapshot of FirewallSettings (safe Dictionary
    /// access on UI thread), then offloads DNS resolution to the thread pool.
    /// </summary>
    private void StartDnsRefreshCycle()
    {
        if (_isRefreshInProgress)
            return;
        var database = databaseProvider.GetDatabase();
        if (database.Accounts.All(a => a.Firewall.IsDefault))
            return;

        // Check for DNS server changes before anything else.
        CheckAndHandleDnsServerChange(database);

        // Local address refresh is synchronous and fast — handle on the UI thread immediately.
        RefreshLocalAddressItems(SnapshotLocalAddressItems(database));

        // Snapshot: collect only entries that need DNS resolution. Dictionary access on UI thread is safe.
        var dnsItems = SnapshotDnsItems(database);
        if (dnsItems.Count == 0)
            return;

        _isRefreshInProgress = true;
        Task.Run(() =>
        {
            try
            {
                ResolveDnsAndRefresh(dnsItems);
            }
            finally
            {
                uiThreadInvoker.Invoke(() => _isRefreshInProgress = false);
            }
        });
    }

    private void ResolveDnsAndRefresh(
        List<DnsRefreshItem> items)
    {
        foreach (var item in items)
        {
            var preResolved = TryResolveDnsAndGetPreResolved(item.Sid, item.DomainEntries);
            if (preResolved == null)
                continue;

            // Dispatch COM work back to UI thread.
            uiThreadInvoker.Invoke(() =>
            {
                try
                {
                    firewallService.RefreshAllowlistRules(item.Sid, item.Username, item.Settings, preResolved);
                    log.Info($"FirewallDnsRefreshService: Refreshed rules for {item.Sid} due to DNS change");
                }
                catch (Exception ex)
                {
                    log.Error($"FirewallDnsRefreshService: Failed to refresh rules for {item.Sid}", ex);
                }
            });
        }
    }

    /// <summary>
    /// Synchronously processes a DNS refresh cycle on the calling thread. Used directly by tests.
    /// Production code uses <see cref="StartDnsRefreshCycle"/> (via timer) which offloads DNS
    /// resolution to the thread pool.
    /// </summary>
    public void ProcessDnsRefresh()
    {
        var database = databaseProvider.GetDatabase();
        if (database.Accounts.All(a => a.Firewall.IsDefault))
            return;

        CheckAndHandleDnsServerChange(database);
        RefreshLocalAddressItems(SnapshotLocalAddressItems(database));

        var items = SnapshotDnsItems(database);
        foreach (var item in items)
        {
            var preResolved = TryResolveDnsAndGetPreResolved(item.Sid, item.DomainEntries);
            if (preResolved == null)
                continue;

            try
            {
                firewallService.RefreshAllowlistRules(item.Sid, item.Username, item.Settings, preResolved);
                log.Info($"FirewallDnsRefreshService: Refreshed rules for {item.Sid} due to DNS change");
            }
            catch (Exception ex)
            {
                log.Error($"FirewallDnsRefreshService: Failed to refresh rules for {item.Sid}", ex);
            }
        }
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

    private void RefreshLocalAddressItems(List<LocalAddressRefreshItem> items)
    {
        foreach (var item in items)
        {
            try
            {
                if (firewallService.RefreshLocalAddressRules(item.Sid, item.Username, item.Settings))
                    log.Info($"FirewallDnsRefreshService: Refreshed local address rules for {item.Sid} due to interface change");
            }
            catch (Exception ex)
            {
                log.Error($"FirewallDnsRefreshService: Failed to refresh local address rules for {item.Sid}", ex);
            }
        }
    }

    /// <summary>
    /// Builds a snapshot of firewall settings entries that require DNS resolution.
    /// Filters out entries with <c>AllowInternet</c> or no domain allowlist entries.
    /// </summary>
    private static List<DnsRefreshItem> SnapshotDnsItems(AppDatabase database)
    {
        var items = new List<DnsRefreshItem>();
        foreach (var account in database.Accounts)
        {
            var settings = account.Firewall;
            if (settings.IsDefault || settings is { AllowInternet: true, AllowLan: true })
                continue;
            var domainEntries = settings.Allowlist.Where(e => e.IsDomain).ToList();
            if (domainEntries.Count == 0)
                continue;
            var username = database.SidNames.TryGetValue(account.Sid, out var name) ? name : account.Sid;
            items.Add(new DnsRefreshItem(account.Sid, username, settings, domainEntries));
        }

        return items;
    }

    /// <summary>
    /// Resolves DNS for the given domain entries. Returns a pre-resolved map (for
    /// <see cref="IFirewallService.RefreshAllowlistRules"/>) if DNS results changed since the
    /// last check, or null if unchanged or resolution failed.
    /// Updates <see cref="_lastResolvedCache"/> on change.
    /// </summary>
    private IReadOnlyDictionary<string, IReadOnlyList<string>>? TryResolveDnsAndGetPreResolved(
        string sid, List<FirewallAllowlistEntry> domainEntries)
    {
        Dictionary<string, List<string>> freshResolved;
        try
        {
            freshResolved = firewallNetworkInfo.ResolveDomainEntriesAsync(domainEntries)
                .GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            log.Warn($"FirewallDnsRefreshService: DNS resolution failed for {sid}: {ex.Message}");
            return null;
        }

        Dictionary<string, List<string>>? cached;
        lock (_cacheLock)
        {
            cached = _lastResolvedCache.GetValueOrDefault(sid);
        }

        if (!HasChanged(cached, freshResolved))
            return null;

        lock (_cacheLock)
        {
            _lastResolvedCache[sid] = freshResolved;
        }

        return freshResolved.ToDictionary(
            kvp => kvp.Key,
            kvp => (IReadOnlyList<string>)kvp.Value,
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Checks if DNS servers have changed since the last cycle. If so, refreshes allowlist rules
    /// for all affected accounts using the cached pre-resolved domains (no new DNS resolution needed).
    /// Called on the UI thread.
    /// </summary>
    private void CheckAndHandleDnsServerChange(AppDatabase database)
    {
        var currentServers = firewallNetworkInfo.GetDnsServerAddresses();
        if (DnsServersEqual(_lastDnsServers, currentServers))
            return;

        log.Info("FirewallDnsRefreshService: DNS server change detected, refreshing allowlist rules");
        _lastDnsServers = currentServers;
        RefreshAllForDnsChange(database);
    }

    /// <summary>
    /// Refreshes allowlist firewall rules for all accounts that have a non-empty allowlist
    /// and at least one internet/LAN block active. Uses cached resolved domains — no new DNS
    /// resolution occurs.
    /// </summary>
    private void RefreshAllForDnsChange(AppDatabase database)
    {
        foreach (var account in database.Accounts)
        {
            var settings = account.Firewall;
            if (settings.IsDefault || settings is { AllowInternet: true, AllowLan: true })
                continue;
            if (settings.Allowlist.Count == 0)
                continue;

            Dictionary<string, List<string>>? cachedEntry;
            lock (_cacheLock)
            {
                _lastResolvedCache.TryGetValue(account.Sid, out cachedEntry);
            }

            var preResolved = cachedEntry?.ToDictionary(kvp => kvp.Key, kvp => (IReadOnlyList<string>)kvp.Value, StringComparer.OrdinalIgnoreCase);

            var username = database.SidNames.TryGetValue(account.Sid, out var name) ? name : account.Sid;
            try
            {
                firewallService.RefreshAllowlistRules(account.Sid, username, settings, preResolved);
                log.Info($"FirewallDnsRefreshService: Refreshed rules for {account.Sid} due to DNS server change");
            }
            catch (Exception ex)
            {
                log.Error($"FirewallDnsRefreshService: Failed to refresh rules for {account.Sid} on DNS server change", ex);
            }
        }
    }

    private static bool DnsServersEqual(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        if (a.Count != b.Count)
            return false;
        var setA = new HashSet<string>(a, StringComparer.OrdinalIgnoreCase);
        return b.All(s => setA.Contains(s));
    }

    private void InitializeCache(AppDatabase database)
    {
        _lastDnsServers = firewallNetworkInfo.GetDnsServerAddresses();

        foreach (var account in database.Accounts)
        {
            var settings = account.Firewall;
            if (settings.IsDefault || settings is { AllowInternet: true, AllowLan: true })
                continue;
            var domainEntries = settings.Allowlist.Where(e => e.IsDomain).ToList();
            if (domainEntries.Count == 0)
                continue;

            try
            {
                var resolveTask = firewallNetworkInfo.ResolveDomainEntriesAsync(domainEntries);
                if (resolveTask.Wait(TimeSpan.FromSeconds(5)))
                    lock (_cacheLock)
                    {
                        _lastResolvedCache[account.Sid] = resolveTask.Result;
                    }
                else
                    log.Warn($"FirewallDnsRefreshService: DNS resolution timed out for {account.Sid} during startup cache init");
            }
            catch (Exception ex)
            {
                log.Warn($"FirewallDnsRefreshService: Failed to initialize cache for {account.Sid}: {ex.Message}");
            }
        }
    }

    private static bool HasChanged(
        Dictionary<string, List<string>>? cached,
        Dictionary<string, List<string>> fresh)
    {
        if (cached == null)
            return true;
        if (cached.Count != fresh.Count)
            return true;

        foreach (var (domain, ips) in fresh)
        {
            if (!cached.TryGetValue(domain, out var cachedIps))
                return true;
            if (!cachedIps.OrderBy(x => x).SequenceEqual(ips.OrderBy(x => x)))
                return true;
        }

        return false;
    }

    public void Dispose()
    {
        _disposed = true;
        _timer?.Dispose();
        _timer = null;
    }

    private record DnsRefreshItem(
        string Sid,
        string Username,
        FirewallAccountSettings Settings,
        List<FirewallAllowlistEntry> DomainEntries);

    private record LocalAddressRefreshItem(string Sid, string Username, FirewallAccountSettings Settings);
}