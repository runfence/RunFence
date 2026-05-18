using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
namespace RunFence.Firewall;

/// <summary>
/// Periodically re-resolves domain allowlist entries and refreshes firewall rules when
/// DNS, local interface, or retry state says existing rules may be stale.
/// </summary>
public class FirewallDnsRefreshService : IDisposable, IBackgroundService, IFirewallDomainRefreshRequester
{
    private readonly FirewallResolvedDomainCache _domainCache;
    private readonly FirewallEnforcementRetryState _retryState;
    private readonly ILoggingService _log;
    private readonly UiThreadDatabaseAccessor _db;
    private readonly FirewallDomainBatchResolver _batchResolver;
    private readonly FirewallDnsRefreshCycleRunner _cycleRunner;
    private readonly FirewallEnforcementRetryProcessor _retryProcessor;
    private readonly FirewallRefreshScheduler _scheduler;

    private volatile bool _disposed;

    public FirewallDnsRefreshService(
        FirewallDnsRefreshCycleRunner cycleRunner,
        FirewallEnforcementRetryProcessor retryProcessor,
        FirewallResolvedDomainCache domainCache,
        FirewallEnforcementRetryState retryState,
        ILoggingService log,
        UiThreadDatabaseAccessor db,
        FirewallDomainBatchResolver batchResolver,
        ITimerScheduler timerScheduler)
    {
        _domainCache = domainCache;
        _retryState = retryState;
        _log = log;
        _db = db;
        _batchResolver = batchResolver;
        _cycleRunner = cycleRunner;
        _retryProcessor = retryProcessor;
        _scheduler = new FirewallRefreshScheduler(
            timerScheduler,
            log,
            RunRefreshCycleAsync);
    }

    public void Start()
    {
        _log.Info("FirewallDnsRefreshService: starting DNS refresh timer in background.");

        Task.Run(() =>
        {
            InitializeDnsState();
            if (_disposed)
                return;

            _scheduler.Start();
            _log.Info("FirewallDnsRefreshService: timer started.");
        });
    }

    public void RequestRefresh()
    {
        if (_disposed)
            return;

        _scheduler.RequestRefresh();
    }

    /// <summary>
    /// Synchronously processes one DNS refresh cycle for deterministic callsites/tests.
    /// </summary>
    public void ProcessDnsRefresh()
    {
        if (_disposed)
            return;

        try
        {
            RunRefreshCycle(_db.CreateSnapshot());
        }
        catch (Exception ex)
        {
            _log.Error("FirewallDnsRefreshService: DNS refresh cycle failed", ex);
        }
    }

    private Task RunRefreshCycleAsync()
    {
        if (_disposed)
            return Task.CompletedTask;

        var database = _db.CreateSnapshot();
        RunRefreshCycle(database);
        return Task.CompletedTask;
    }

    private void RunRefreshCycle(AppDatabase database)
    {
        _cycleRunner.RunCycle(database);
        _retryProcessor.ProcessRetries(database);
    }

    private void InitializeDnsState()
    {
        try
        {
            var database = _db.CreateSnapshot();
            _domainCache.Prune(database);
            _retryState.Prune(database);
            _retryState.UpdateDnsServersAndReturnChanged(_batchResolver.GetDnsServerAddresses());
        }
        catch (Exception ex)
        {
            _log.Warn($"FirewallDnsRefreshService: failed to initialize DNS refresh state: {ex.Message}");
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _scheduler.Stop();
    }
}
