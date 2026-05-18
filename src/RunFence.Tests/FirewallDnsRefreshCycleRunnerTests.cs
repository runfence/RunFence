using Moq;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Firewall;
using static RunFence.Tests.FirewallTestHelpers;
using Xunit;

namespace RunFence.Tests;

public class FirewallDnsRefreshCycleRunnerTests
{
    private const string Sid = "S-1-5-21-1000-1000-1000-1001";
    private const string Username = "alice";
    private const string Domain = "example.com";

    private readonly Mock<IFirewallDnsRefreshTarget> _refreshTarget = new();
    private readonly Mock<IGlobalIcmpPolicyService> _globalIcmpPolicy = new();
    private readonly Mock<IFirewallNetworkInfo> _networkInfo = new();
    private readonly Mock<ILoggingService> _log = new();
    private readonly FirewallResolvedDomainCache _domainCache = new(new FirewallDomainDirtyTracker());
    private readonly FirewallEnforcementRetryState _retryState = new();

    public FirewallDnsRefreshCycleRunnerTests()
    {
        _networkInfo.Setup(n => n.GetDnsServerAddresses()).Returns([]);
    }

    [Fact]
    public void RunCycle_NoDomainEntriesAndNoDnsServerChange_DoesNotRefreshAllowlist()
    {
        var database = BuildDatabase(BlockInternet(addIpEntry: true));
        var runner = BuildRunner();

        runner.RunCycle(database);

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
    public void RunCycle_DnsChanged_RefreshesAllowlistAndEnforcesGlobalIcmp()
    {
        var database = BuildDatabase(BlockInternet(addDomainEntry: true));
        _networkInfo.Setup(n => n.GetDnsServerAddresses()).Returns(["192.0.2.53"]);
        _networkInfo
            .Setup(n => n.ResolveDomainEntriesAsync(It.IsAny<IReadOnlyList<FirewallAllowlistEntry>>()))
            .ReturnsAsync(Resolved("203.0.113.10"));
        var runner = BuildRunner();

        runner.RunCycle(database);
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
        runner.RunCycle(database);

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
    public void RunCycle_LocalAddressRefreshFailure_LogsAndContinues()
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
        var runner = BuildRunner();

        runner.RunCycle(database);

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
    public void RunCycle_GlobalIcmpFailure_MarksDirtyForRetry()
    {
        var database = BuildDatabase(BlockInternet(addDomainEntry: true));
        _networkInfo
            .Setup(n => n.ResolveDomainEntriesAsync(It.IsAny<IReadOnlyList<FirewallAllowlistEntry>>()))
            .ReturnsAsync(Resolved("203.0.113.10"));
        _globalIcmpPolicy
            .Setup(g => g.EnforceGlobalIcmpBlock(
                It.IsAny<AppDatabase>(),
                It.IsAny<IReadOnlyDictionary<string, IReadOnlyList<string>>>()))
            .Throws(new InvalidOperationException("global ICMP failed"));
        var runner = BuildRunner();

        runner.RunCycle(database);

        Assert.True(_retryState.IsGlobalIcmpDirty());
        _log.Verify(l => l.Error(
            It.Is<string>(message => message.Contains("Failed to enforce global ICMP", StringComparison.Ordinal)),
            It.IsAny<Exception>()), Times.Once);
    }

    private FirewallDnsRefreshCycleRunner BuildRunner()
        => new(
            _refreshTarget.Object,
            _globalIcmpPolicy.Object,
            _domainCache,
            _retryState,
            _log.Object,
            new FirewallDomainBatchResolver(_networkInfo.Object, _log.Object));
}
