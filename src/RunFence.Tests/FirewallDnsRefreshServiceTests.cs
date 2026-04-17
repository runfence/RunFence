using Moq;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Firewall;
using RunFence.Infrastructure;
using RunFence.Persistence;
using Xunit;

namespace RunFence.Tests;

public class FirewallDnsRefreshServiceTests
{
    private const string Sid = "S-1-5-21-1000-1000-1000-1001";
    private const string OtherSid = "S-1-5-21-1000-1000-1000-1002";
    private const string Username = "alice";
    private const string OtherUsername = "bob";
    private const string Domain = "example.com";

    private readonly Mock<IFirewallDnsRefreshTarget> _refreshTarget = new();
    private readonly Mock<IGlobalIcmpPolicyService> _globalIcmpPolicy = new();
    private readonly Mock<IFirewallNetworkInfo> _networkInfo = new();
    private readonly Mock<ILoggingService> _log = new();
    private readonly Mock<IUiThreadInvoker> _uiThreadInvoker = new();
    private readonly FirewallResolvedDomainCache _domainCache = new();
    private readonly FirewallEnforcementRetryState _retryState = new();

    public FirewallDnsRefreshServiceTests()
    {
        _networkInfo.Setup(n => n.GetDnsServerAddresses()).Returns([]);
        _uiThreadInvoker.Setup(u => u.Invoke(It.IsAny<Func<AppDatabase>>()))
            .Returns<Func<AppDatabase>>(f => f());
    }

    [Fact]
    public void ProcessDnsRefresh_NoDomainEntriesAndNoDnsServerChange_DoesNotRefreshAllowlist()
    {
        var database = BuildDatabase(BlockInternet(addIpEntry: true));
        var service = BuildService(database);

        service.ProcessDnsRefresh();

        _refreshTarget.Verify(f => f.RefreshAllowlistRules(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<FirewallAccountSettings>(),
            It.IsAny<IReadOnlyDictionary<string, IReadOnlyList<string>>>()), Times.Never);
        _globalIcmpPolicy.Verify(g => g.EnforceGlobalIcmpBlock(
            It.IsAny<AppDatabase>(),
            It.IsAny<IReadOnlyDictionary<string, IReadOnlyList<string>>>()), Times.Never);
    }

    [Fact]
    public void ProcessDnsRefresh_DnsChanged_RefreshesAllowlistEnforcesGlobalIcmpAndClearsDirtyState()
    {
        var database = BuildDatabase(BlockInternet(addDomainEntry: true));
        _networkInfo
            .Setup(n => n.ResolveDomainEntriesAsync(It.IsAny<IReadOnlyList<FirewallAllowlistEntry>>()))
            .ReturnsAsync(Resolved("203.0.113.10"));
        var service = BuildService(database);

        service.ProcessDnsRefresh();
        _refreshTarget.Verify(f => f.RefreshAllowlistRules(
            Sid,
            Username,
            It.IsAny<FirewallAccountSettings>(),
            It.Is<IReadOnlyDictionary<string, IReadOnlyList<string>>>(cache =>
                cache.ContainsKey(Domain) && cache[Domain].SequenceEqual(new[] { "203.0.113.10" }))), Times.Once);
        _globalIcmpPolicy.Verify(g => g.EnforceGlobalIcmpBlock(
            It.IsAny<AppDatabase>(),
            It.Is<IReadOnlyDictionary<string, IReadOnlyList<string>>>(cache =>
                cache.ContainsKey(Domain) && cache[Domain].SequenceEqual(new[] { "203.0.113.10" }))), Times.Once);

        _refreshTarget.Invocations.Clear();
        _globalIcmpPolicy.Invocations.Clear();
        service.ProcessDnsRefresh();

        _refreshTarget.Verify(f => f.RefreshAllowlistRules(
            Sid,
            Username,
            It.IsAny<FirewallAccountSettings>(),
            It.IsAny<IReadOnlyDictionary<string, IReadOnlyList<string>>>()), Times.Never);
        _globalIcmpPolicy.Verify(g => g.EnforceGlobalIcmpBlock(
            It.IsAny<AppDatabase>(),
            It.IsAny<IReadOnlyDictionary<string, IReadOnlyList<string>>>()), Times.Never);
    }

    [Fact]
    public void ProcessDnsRefresh_SharedDomain_ResolvesOnceAndRefreshesBothAccountsWithGlobalCache()
    {
        var database = BuildDatabase(
            BlockInternet(addDomainEntry: true, domain: "Example.COM"),
            sid: OtherSid,
            username: OtherUsername,
            settings: BlockInternet(addDomainEntry: true, domain: "example.com"));
        _networkInfo
            .Setup(n => n.ResolveDomainEntriesAsync(It.IsAny<IReadOnlyList<FirewallAllowlistEntry>>()))
            .ReturnsAsync((IReadOnlyList<FirewallAllowlistEntry> entries) =>
                new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
                {
                    [entries[0].Value] = ["203.0.113.10"]
                });
        var service = BuildService(database);

        service.ProcessDnsRefresh();

        _networkInfo.Verify(n => n.ResolveDomainEntriesAsync(
            It.Is<IReadOnlyList<FirewallAllowlistEntry>>(entries =>
                entries.Count == 1 && entries[0].Value == "Example.COM")), Times.Once);
        _refreshTarget.Verify(f => f.RefreshAllowlistRules(
            Sid,
            Username,
            It.IsAny<FirewallAccountSettings>(),
            It.Is<IReadOnlyDictionary<string, IReadOnlyList<string>>>(cache =>
                cache.ContainsKey("Example.COM") && cache["Example.COM"].SequenceEqual(new[] { "203.0.113.10" }))), Times.Once);
        _refreshTarget.Verify(f => f.RefreshAllowlistRules(
            OtherSid,
            OtherUsername,
            It.IsAny<FirewallAccountSettings>(),
            It.Is<IReadOnlyDictionary<string, IReadOnlyList<string>>>(cache =>
                cache.ContainsKey("example.com") && cache["example.com"].SequenceEqual(new[] { "203.0.113.10" }))), Times.Once);
    }

    [Fact]
    public void ProcessDnsRefresh_DnsUnchangedButDirty_RefreshesAllowlistAndClearsDirtyAfterSuccess()
    {
        var database = BuildDatabase(BlockInternet(addDomainEntry: true));
        SeedCleanCache("203.0.113.10");
        _domainCache.MarkDirty(Sid, [Domain]);
        _networkInfo
            .Setup(n => n.ResolveDomainEntriesAsync(It.IsAny<IReadOnlyList<FirewallAllowlistEntry>>()))
            .ReturnsAsync(Resolved("203.0.113.10"));
        var service = BuildService(database);

        service.ProcessDnsRefresh();
        _refreshTarget.Invocations.Clear();
        _globalIcmpPolicy.Invocations.Clear();
        service.ProcessDnsRefresh();

        _refreshTarget.Verify(f => f.RefreshAllowlistRules(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<FirewallAccountSettings>(),
            It.IsAny<IReadOnlyDictionary<string, IReadOnlyList<string>>>()), Times.Never);
        _globalIcmpPolicy.Verify(g => g.EnforceGlobalIcmpBlock(
            It.IsAny<AppDatabase>(),
            It.IsAny<IReadOnlyDictionary<string, IReadOnlyList<string>>>()), Times.Never);
    }

    [Fact]
    public void ProcessDnsRefresh_DnsResolutionThrows_LogsWarningAndKeepsDirtyState()
    {
        var database = BuildDatabase(BlockInternet(addDomainEntry: true));
        _domainCache.MarkDirty(Sid, [Domain]);
        _networkInfo
            .Setup(n => n.ResolveDomainEntriesAsync(It.IsAny<IReadOnlyList<FirewallAllowlistEntry>>()))
            .ThrowsAsync(new InvalidOperationException("DNS timeout"));
        var service = BuildService(database);

        service.ProcessDnsRefresh();
        var dirtyDecision = _domainCache.GetRefreshDecision(Sid, database.GetOrCreateAccount(Sid).Firewall, EmptyChangedDomains());

        Assert.True(dirtyDecision.WasDirty);
        _log.Verify(l => l.Warn(It.Is<string>(message => message.Contains("DNS resolution failed", StringComparison.Ordinal))), Times.Once);
        _refreshTarget.Verify(f => f.RefreshAllowlistRules(
            Sid,
            Username,
            It.IsAny<FirewallAccountSettings>(),
            It.Is<IReadOnlyDictionary<string, IReadOnlyList<string>>>(cache => cache.Count == 0)), Times.Once);
    }

    [Fact]
    public void ProcessDnsRefresh_WhenOneDomainMissingFromResult_CachesSuccessfulDomainAndRefreshesMatchingRules()
    {
        var database = BuildDatabase(BlockInternetWithDomains("bad.example", "good.example"));
        _networkInfo
            .Setup(n => n.ResolveDomainEntriesAsync(It.IsAny<IReadOnlyList<FirewallAllowlistEntry>>()))
            .ReturnsAsync(new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["good.example"] = ["203.0.113.20"]
            });
        var service = BuildService(database);

        service.ProcessDnsRefresh();

        _refreshTarget.Verify(f => f.RefreshAllowlistRules(
            Sid,
            Username,
            It.IsAny<FirewallAccountSettings>(),
            It.Is<IReadOnlyDictionary<string, IReadOnlyList<string>>>(cache =>
                cache.Count == 1
                && cache.ContainsKey("good.example")
                && cache["good.example"].SequenceEqual(new[] { "203.0.113.20" }))), Times.Once);
        _log.Verify(l => l.Warn(It.Is<string>(message => message.Contains("DNS returned no addresses", StringComparison.Ordinal))), Times.Once);
    }

    [Fact]
    public void ProcessDnsRefresh_DnsReturnsNoAddresses_LogsWarningAndKeepsDirtyState()
    {
        var database = BuildDatabase(BlockInternet(addDomainEntry: true));
        _domainCache.MarkDirty(Sid, [Domain]);
        _networkInfo
            .Setup(n => n.ResolveDomainEntriesAsync(It.IsAny<IReadOnlyList<FirewallAllowlistEntry>>()))
            .ReturnsAsync(new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            {
                [Domain] = []
            });
        var service = BuildService(database);

        service.ProcessDnsRefresh();
        var dirtyDecision = _domainCache.GetRefreshDecision(Sid, database.GetOrCreateAccount(Sid).Firewall, EmptyChangedDomains());

        Assert.True(dirtyDecision.WasDirty);
        _log.Verify(l => l.Warn(It.Is<string>(message => message.Contains("DNS returned no addresses", StringComparison.Ordinal))), Times.Once);
        _refreshTarget.Verify(f => f.RefreshAllowlistRules(
            Sid,
            Username,
            It.IsAny<FirewallAccountSettings>(),
            It.Is<IReadOnlyDictionary<string, IReadOnlyList<string>>>(cache => cache.Count == 0)), Times.Once);
    }

    [Fact]
    public void ProcessDnsRefresh_AllowlistRefreshThrows_LogsErrorKeepsDirtyAndContinues()
    {
        var database = BuildDatabase(
            BlockInternet(addDomainEntry: true),
            sid: OtherSid,
            username: OtherUsername,
            settings: BlockInternet(addDomainEntry: true, domain: "second.example"));
        _networkInfo
            .Setup(n => n.ResolveDomainEntriesAsync(It.IsAny<IReadOnlyList<FirewallAllowlistEntry>>()))
            .ReturnsAsync(new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            {
                [Domain] = ["203.0.113.10"],
                ["second.example"] = ["203.0.113.11"]
            });
        _refreshTarget
            .Setup(f => f.RefreshAllowlistRules(
                Sid,
                Username,
                It.IsAny<FirewallAccountSettings>(),
                It.IsAny<IReadOnlyDictionary<string, IReadOnlyList<string>>>()))
            .Throws(new InvalidOperationException("firewall unavailable"));
        var service = BuildService(database);

        service.ProcessDnsRefresh();
        var dirtyDecision = _domainCache.GetRefreshDecision(Sid, database.GetOrCreateAccount(Sid).Firewall, EmptyChangedDomains());

        Assert.True(dirtyDecision.WasDirty);
        _refreshTarget.Verify(f => f.RefreshAllowlistRules(
            OtherSid,
            OtherUsername,
            It.IsAny<FirewallAccountSettings>(),
            It.IsAny<IReadOnlyDictionary<string, IReadOnlyList<string>>>()), Times.Once);
        _log.Verify(l => l.Error(
            It.Is<string>(message => message.Contains("Failed to refresh allowlist rules", StringComparison.Ordinal)),
            It.IsAny<Exception>()), Times.Once);
    }

    [Fact]
    public void ProcessDnsRefresh_SharedDomainRefreshFailure_KeepsFailedSidDirtyAfterOtherSidSucceeds()
    {
        var settings = BlockInternet(addDomainEntry: true);
        var database = BuildDatabase(
            settings,
            sid: OtherSid,
            username: OtherUsername,
            settings: BlockInternet(addDomainEntry: true));
        SeedCleanCache("203.0.113.10");
        _domainCache.MarkDirty(Sid, [Domain]);
        _domainCache.MarkDirty(OtherSid, [Domain]);
        _networkInfo
            .Setup(n => n.ResolveDomainEntriesAsync(It.IsAny<IReadOnlyList<FirewallAllowlistEntry>>()))
            .ReturnsAsync(Resolved("203.0.113.10"));
        _refreshTarget
            .Setup(f => f.RefreshAllowlistRules(
                Sid,
                Username,
                It.IsAny<FirewallAccountSettings>(),
                It.IsAny<IReadOnlyDictionary<string, IReadOnlyList<string>>>()))
            .Throws(new InvalidOperationException("firewall unavailable"));
        var service = BuildService(database);

        service.ProcessDnsRefresh();

        Assert.True(_domainCache.GetRefreshDecision(Sid, settings, EmptyChangedDomains()).WasDirty);
        Assert.False(_domainCache.GetRefreshDecision(OtherSid, settings, EmptyChangedDomains()).WasDirty);
    }

    [Fact]
    public void ProcessDnsRefresh_DnsServerChange_RefreshesAffectedAccountsWithCachedDomainsAndClearsPendingAfterSuccess()
    {
        var database = BuildDatabase(BlockInternet(addIpEntry: true));
        _networkInfo.Setup(n => n.GetDnsServerAddresses()).Returns(["192.0.2.53"]);
        var service = BuildService(database);

        service.ProcessDnsRefresh();

        Assert.Empty(_retryState.GetDnsServerRefreshPendingSids());
        _refreshTarget.Verify(f => f.RefreshAllowlistRules(
            Sid,
            Username,
            It.IsAny<FirewallAccountSettings>(),
            It.Is<IReadOnlyDictionary<string, IReadOnlyList<string>>>(cache => cache.Count == 0)), Times.Once);
        _globalIcmpPolicy.Verify(g => g.EnforceGlobalIcmpBlock(
            It.IsAny<AppDatabase>(),
            It.IsAny<IReadOnlyDictionary<string, IReadOnlyList<string>>>()), Times.Once);
        _networkInfo.Verify(n => n.ResolveDomainEntriesAsync(It.IsAny<IReadOnlyList<FirewallAllowlistEntry>>()), Times.Never);
    }

    [Fact]
    public void ProcessDnsRefresh_DnsServerRefreshFailure_KeepsPendingSid()
    {
        var database = BuildDatabase(BlockInternet(addIpEntry: true));
        _networkInfo.Setup(n => n.GetDnsServerAddresses()).Returns(["192.0.2.53"]);
        _refreshTarget
            .Setup(f => f.RefreshAllowlistRules(
                Sid,
                Username,
                It.IsAny<FirewallAccountSettings>(),
                It.IsAny<IReadOnlyDictionary<string, IReadOnlyList<string>>>()))
            .Throws(new InvalidOperationException("firewall unavailable"));
        var service = BuildService(database);

        service.ProcessDnsRefresh();

        Assert.Contains(Sid, _retryState.GetDnsServerRefreshPendingSids());
        _log.Verify(l => l.Error(
            It.Is<string>(message => message.Contains("Failed to refresh allowlist rules", StringComparison.Ordinal)),
            It.IsAny<Exception>()), Times.Once);
    }

    [Fact]
    public void ProcessDnsRefresh_LocalAddressRefreshFailure_LogsAndStillRefreshesAllowlist()
    {
        var settings = BlockInternet(addDomainEntry: true, allowLocalhost: false);
        var database = BuildDatabase(settings);
        _networkInfo
            .Setup(n => n.ResolveDomainEntriesAsync(It.IsAny<IReadOnlyList<FirewallAllowlistEntry>>()))
            .ReturnsAsync(Resolved("203.0.113.10"));
        _refreshTarget
            .Setup(f => f.RefreshLocalAddressRules(
                Sid,
                Username,
                It.IsAny<FirewallAccountSettings>()))
            .Throws(new InvalidOperationException("local refresh failed"));
        var service = BuildService(database);

        service.ProcessDnsRefresh();

        _refreshTarget.Verify(f => f.RefreshAllowlistRules(
            Sid,
            Username,
            It.IsAny<FirewallAccountSettings>(),
            It.IsAny<IReadOnlyDictionary<string, IReadOnlyList<string>>>()), Times.Once);
        _log.Verify(l => l.Error(
            It.Is<string>(message => message.Contains("Failed to refresh local address rules", StringComparison.Ordinal)),
            It.IsAny<Exception>()), Times.Once);
    }

    [Fact]
    public void ProcessDnsRefresh_GlobalIcmpFailure_LogsMarksDirtyAndLaterSuccessClearsDirty()
    {
        var database = BuildDatabase(BlockInternet(addDomainEntry: true));
        _networkInfo
            .Setup(n => n.ResolveDomainEntriesAsync(It.IsAny<IReadOnlyList<FirewallAllowlistEntry>>()))
            .ReturnsAsync(Resolved("203.0.113.10"));
        _globalIcmpPolicy
            .SetupSequence(g => g.EnforceGlobalIcmpBlock(
                It.IsAny<AppDatabase>(),
                It.IsAny<IReadOnlyDictionary<string, IReadOnlyList<string>>>()))
            .Throws(new InvalidOperationException("global ICMP failed"))
            .Pass();
        var service = BuildService(database);

        var exception = Record.Exception(() => service.ProcessDnsRefresh());
        Assert.Null(exception);
        Assert.True(_retryState.IsGlobalIcmpDirty());

        service.ProcessDnsRefresh();

        Assert.False(_retryState.IsGlobalIcmpDirty());
        _globalIcmpPolicy.Verify(g => g.EnforceGlobalIcmpBlock(
            It.IsAny<AppDatabase>(),
            It.IsAny<IReadOnlyDictionary<string, IReadOnlyList<string>>>()), Times.Exactly(2));
        _log.Verify(l => l.Error(
            It.Is<string>(message => message.Contains("Failed to enforce global ICMP", StringComparison.Ordinal)),
            It.IsAny<Exception>()), Times.Once);
    }

    [Fact]
    public void ProcessDnsRefresh_AllAccountsDefaultButGlobalIcmpDirty_EnforcesGlobalIcmp()
    {
        var database = BuildDatabase(new FirewallAccountSettings());
        _retryState.MarkGlobalIcmpDirty();
        var service = BuildService(database);

        service.ProcessDnsRefresh();

        Assert.False(_retryState.IsGlobalIcmpDirty());
        _globalIcmpPolicy.Verify(g => g.EnforceGlobalIcmpBlock(
            It.IsAny<AppDatabase>(),
            It.IsAny<IReadOnlyDictionary<string, IReadOnlyList<string>>>()), Times.Once);
    }

    [Fact]
    public void ProcessDnsRefresh_PrunesDomainCacheAndRetryStateAgainstFreshSnapshot()
    {
        _domainCache.UpdateResolvedDomainsAndGetChangedDomains([Domain], Resolved("203.0.113.10"));
        _domainCache.MarkDirty(Sid, [Domain]);
        _retryState.MarkDnsServerRefreshPending([Sid]);
        var service = BuildService(new AppDatabase());

        service.ProcessDnsRefresh();

        Assert.Empty(_domainCache.GetAccountSnapshot(BlockInternet(addDomainEntry: true)));
        Assert.Empty(_retryState.GetDnsServerRefreshPendingSids());
    }

    [Fact]
    public void ProcessDnsRefresh_CreatesDatabaseSnapshotOnUiThread()
    {
        bool insideUiInvoke = false;
        bool providerCalledInsideUiInvoke = false;
        var database = BuildDatabase(BlockInternet(addIpEntry: true));
        _uiThreadInvoker
            .Setup(u => u.Invoke(It.IsAny<Func<AppDatabase>>()))
            .Returns<Func<AppDatabase>>(f =>
            {
                insideUiInvoke = true;
                try
                {
                    return f();
                }
                finally
                {
                    insideUiInvoke = false;
                }
            });
        var service = BuildService(() =>
        {
            providerCalledInsideUiInvoke = insideUiInvoke;
            return database;
        });

        service.ProcessDnsRefresh();

        Assert.True(providerCalledInsideUiInvoke);
    }

    [Fact]
    public void ProcessDnsRefresh_CreatesDatabaseSnapshotBeforeStartingBackgroundRefreshWork()
    {
        int callerThreadId = Environment.CurrentManagedThreadId;
        bool invokedOnCallerThread = false;
        var service = BuildService(BuildDatabase(BlockInternet(addIpEntry: true)));
        _uiThreadInvoker
            .Setup(u => u.Invoke(It.IsAny<Func<AppDatabase>>()))
            .Returns<Func<AppDatabase>>(f =>
            {
                if (Environment.CurrentManagedThreadId != callerThreadId)
                    throw new InvalidOperationException("Snapshot was requested from the refresh worker.");

                invokedOnCallerThread = true;
                return f();
            });

        service.ProcessDnsRefresh();

        Assert.True(invokedOnCallerThread);
        _log.Verify(l => l.Error(
            It.Is<string>(message => message.Contains("DNS refresh cycle failed", StringComparison.Ordinal)),
            It.IsAny<Exception>()), Times.Never);
    }

    [Fact]
    public void ProcessDnsRefresh_WhenUiThreadSnapshotFails_LogsAndReleasesWorker()
    {
        int invokeCalls = 0;
        var service = BuildService(BuildDatabase(BlockInternet(addIpEntry: true)));
        _uiThreadInvoker
            .Setup(u => u.Invoke(It.IsAny<Func<AppDatabase>>()))
            .Returns<Func<AppDatabase>>(f =>
            {
                if (Interlocked.Increment(ref invokeCalls) == 1)
                    throw new InvalidOperationException("UI unavailable");

                return f();
            });

        service.ProcessDnsRefresh();
        service.ProcessDnsRefresh();

        _log.Verify(l => l.Error(
            It.Is<string>(message => message.Contains("DNS refresh cycle failed", StringComparison.Ordinal)),
            It.IsAny<Exception>()), Times.Once);
    }

    [Fact]
    public void RequestRefresh_MultipleRequestsWhileCycleRuns_CoalesceIntoOneFollowUpCycle()
    {
        var firstCycleEntered = new ManualResetEventSlim();
        var releaseFirstCycle = new ManualResetEventSlim();
        var secondCycleEntered = new ManualResetEventSlim();
        int snapshotCount = 0;
        var database = BuildDatabase(BlockInternet(addIpEntry: true, allowLocalhost: false));
        using var service = BuildService(() =>
        {
            int count = Interlocked.Increment(ref snapshotCount);
            if (count == 1)
            {
                firstCycleEntered.Set();
                releaseFirstCycle.Wait(TimeSpan.FromSeconds(5));
            }
            else if (count == 2)
            {
                secondCycleEntered.Set();
            }

            return database;
        });

        service.RequestRefresh();
        Assert.True(firstCycleEntered.Wait(TimeSpan.FromSeconds(5)));

        service.RequestRefresh();
        service.RequestRefresh();
        service.RequestRefresh();
        releaseFirstCycle.Set();

        Assert.True(secondCycleEntered.Wait(TimeSpan.FromSeconds(5)));
        Assert.Equal(2, Volatile.Read(ref snapshotCount));
    }

    [Fact]
    public void RequestRefresh_WorkerStateResetsAfterUnexpectedCycleException()
    {
        var firstFailureLogged = new ManualResetEventSlim();
        var secondCycleEntered = new ManualResetEventSlim();
        bool throwFromProvider = true;
        var database = BuildDatabase(BlockInternet(addIpEntry: true, allowLocalhost: false));
        using var service = BuildService(() =>
        {
            if (throwFromProvider)
                throw new InvalidOperationException("snapshot failed");

            secondCycleEntered.Set();
            return database;
        });
        _log
            .Setup(l => l.Error(
                It.Is<string>(message => message.Contains("DNS refresh cycle failed", StringComparison.Ordinal)),
                It.IsAny<Exception>()))
            .Callback(() => firstFailureLogged.Set());

        service.RequestRefresh();
        Assert.True(firstFailureLogged.Wait(TimeSpan.FromSeconds(5)));

        throwFromProvider = false;
        service.RequestRefresh();

        Assert.True(secondCycleEntered.Wait(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void ProcessDnsRefresh_WhenWorkerIsRunning_QueuesFollowUpWithoutOverlapping()
    {
        var firstRefreshEntered = new ManualResetEventSlim();
        var releaseFirstRefresh = new ManualResetEventSlim();
        var secondRefreshEntered = new ManualResetEventSlim();
        int activeRefreshes = 0;
        int maxActiveRefreshes = 0;
        int refreshCalls = 0;
        var database = BuildDatabase(BlockInternet(addIpEntry: true, allowLocalhost: false));
        _refreshTarget
            .Setup(f => f.RefreshLocalAddressRules(
                Sid,
                Username,
                It.IsAny<FirewallAccountSettings>()))
            .Returns(() =>
            {
                int active = Interlocked.Increment(ref activeRefreshes);
                UpdateMax(ref maxActiveRefreshes, active);
                int call = Interlocked.Increment(ref refreshCalls);
                if (call == 1)
                {
                    firstRefreshEntered.Set();
                    releaseFirstRefresh.Wait(TimeSpan.FromSeconds(5));
                }
                else if (call == 2)
                {
                    secondRefreshEntered.Set();
                }

                Interlocked.Decrement(ref activeRefreshes);
                return false;
            });
        using var service = BuildService(database);

        service.RequestRefresh();
        Assert.True(firstRefreshEntered.Wait(TimeSpan.FromSeconds(5)));

        service.ProcessDnsRefresh();
        releaseFirstRefresh.Set();

        Assert.True(secondRefreshEntered.Wait(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, Volatile.Read(ref maxActiveRefreshes));
        Assert.Equal(2, Volatile.Read(ref refreshCalls));
    }

    private FirewallDnsRefreshService BuildService(AppDatabase database) =>
        BuildService(() => database);

    private FirewallDnsRefreshService BuildService(Func<AppDatabase> getDatabase) =>
        new(
            _refreshTarget.Object,
            _globalIcmpPolicy.Object,
            _domainCache,
            _retryState,
            _log.Object,
            new UiThreadDatabaseAccessor(new LambdaDatabaseProvider(getDatabase), _uiThreadInvoker.Object),
            _networkInfo.Object);

    private void SeedCleanCache(string address)
    {
        _domainCache.UpdateResolvedDomainsAndGetChangedDomains([Domain], Resolved(address));
    }

    private static void UpdateMax(ref int target, int value)
    {
        while (true)
        {
            int current = Volatile.Read(ref target);
            if (value <= current)
                return;
            if (Interlocked.CompareExchange(ref target, value, current) == current)
                return;
        }
    }

    private static IReadOnlySet<string> EmptyChangedDomains() => FirewallTestHelpers.EmptyChangedDomains();

    private static Dictionary<string, List<string>> Resolved(string address) => FirewallTestHelpers.Resolved(address);

    private static FirewallAccountSettings BlockInternet(
        bool addDomainEntry = false,
        bool addIpEntry = false,
        bool allowLocalhost = true,
        string domain = Domain)
        => FirewallTestHelpers.BlockInternet(addDomainEntry, addIpEntry, allowLocalhost, domain);

    private static FirewallAccountSettings BlockInternetWithDomains(params string[] domains)
        => FirewallTestHelpers.BlockInternetWithDomains(domains);

    private static AppDatabase BuildDatabase(FirewallAccountSettings settings) => FirewallTestHelpers.BuildDatabase(settings);

    private static AppDatabase BuildDatabase(
        FirewallAccountSettings firstSettings,
        string sid,
        string username,
        FirewallAccountSettings settings)
        => FirewallTestHelpers.BuildDatabase(firstSettings, sid, username, settings);
}
