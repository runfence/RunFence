using Moq;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Firewall;
using RunFence.Firewall.Wfp;
using Xunit;

namespace RunFence.Tests;

public class FirewallGlobalIcmpEnforcerTests
{
    private const string FirstSid = "S-1-5-21-1000-1000-1000-1001";
    private const string SecondSid = "S-1-5-21-2000-2000-2000-2002";

    private readonly Mock<ILoggingService> _log = new();
    private readonly Mock<INetworkInterfaceInfoProvider> _networkInfo = new();
    private readonly Mock<IWfpGlobalIcmpBlocker> _wfpGlobalIcmpBlocker = new();

    private FirewallGlobalIcmpEnforcer BuildEnforcer()
    {
        _networkInfo.Setup(n => n.GetDnsServerAddresses()).Returns([]);
        _networkInfo.Setup(n => n.GetLocalAddresses()).Returns([]);
        var rangeBuilder = new FirewallAddressRangeBuilder();
        var addressBuilder = new FirewallAddressExclusionBuilder(rangeBuilder, _networkInfo.Object);
        return new FirewallGlobalIcmpEnforcer(
            _log.Object,
            rangeBuilder,
            addressBuilder,
            _wfpGlobalIcmpBlocker.Object);
    }

    [Fact]
    public void EnforceGlobalIcmpBlock_WhenSettingDisabled_RemovesGlobalBlock()
    {
        var database = new AppDatabase();
        database.Settings.BlockIcmpWhenInternetBlocked = false;
        database.Accounts.Add(new AccountEntry
        {
            Sid = FirstSid,
            Firewall = new FirewallAccountSettings { AllowInternet = false }
        });

        BuildEnforcer().EnforceGlobalIcmpBlock(database, new Dictionary<string, IReadOnlyList<string>>());

        _wfpGlobalIcmpBlocker.Verify(
            w => w.Apply(
                It.Is<IReadOnlyList<string>>(ranges => ranges.Count == 0),
                It.Is<IReadOnlyList<string>>(ranges => ranges.Count == 0)),
            Times.Once);
    }

    [Fact]
    public void EnforceGlobalIcmpBlock_WhenNoBlockedAccounts_RemovesGlobalBlock()
    {
        var database = new AppDatabase();
        database.Accounts.Add(new AccountEntry
        {
            Sid = FirstSid,
            Firewall = new FirewallAccountSettings { AllowInternet = true }
        });

        BuildEnforcer().EnforceGlobalIcmpBlock(database, new Dictionary<string, IReadOnlyList<string>>());

        _wfpGlobalIcmpBlocker.Verify(
            w => w.Apply(
                It.Is<IReadOnlyList<string>>(ranges => ranges.Count == 0),
                It.Is<IReadOnlyList<string>>(ranges => ranges.Count == 0)),
            Times.Once);
    }

    [Fact]
    public void EnforceGlobalIcmpBlock_UsesCommonCachedDomainExclusionsForCidrs()
    {
        const string cachedDomain = "cached.example";
        const string cachedIp = "203.0.113.10";
        var database = new AppDatabase();
        database.Accounts.Add(BlockedAccountWithDomain(FirstSid, cachedDomain));
        database.Accounts.Add(BlockedAccountWithDomain(SecondSid, cachedDomain));
        var cache = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [cachedDomain] = [cachedIp]
        };
        var rangeBuilder = new FirewallAddressRangeBuilder();
        var expectedIpv4 = SplitCidrs(rangeBuilder.BuildInternetIPv4Range([cachedIp]));
        var expectedIpv6 = SplitCidrs(rangeBuilder.BuildInternetIPv6Range([cachedIp]));

        BuildEnforcer().EnforceGlobalIcmpBlock(database, cache);

        _wfpGlobalIcmpBlocker.Verify(
            w => w.Apply(
                It.Is<IReadOnlyList<string>>(ranges => ranges.SequenceEqual(expectedIpv4)),
                It.Is<IReadOnlyList<string>>(ranges => ranges.SequenceEqual(expectedIpv6))),
            Times.Once);
    }

    [Fact]
    public void CreateGlobalIcmpPlan_ReturnsCommonDomainExclusionsWithoutApplyingWfp()
    {
        const string cachedDomain = "cached.example";
        const string cachedIp = "203.0.113.10";
        var database = new AppDatabase();
        database.Accounts.Add(BlockedAccountWithDomain(FirstSid, cachedDomain));
        database.Accounts.Add(BlockedAccountWithDomain(SecondSid, cachedDomain));
        var cache = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [cachedDomain] = [cachedIp]
        };

        var plan = BuildEnforcer().CreateGlobalIcmpPlan(database, cache);

        Assert.True(plan.Enabled);
        Assert.Equal(2, plan.BlockedAccountCount);
        Assert.Equal([cachedIp], plan.CommonExclusions);
        _wfpGlobalIcmpBlocker.Verify(w => w.Apply(
            It.IsAny<IReadOnlyList<string>>(),
            It.IsAny<IReadOnlyList<string>>()), Times.Never);
    }

    [Fact]
    public void EnforceGlobalIcmpBlock_WithPlan_AppliesPlannedRanges()
    {
        const string cachedIp = "203.0.113.10";
        var rangeBuilder = new FirewallAddressRangeBuilder();
        var expectedIpv4 = SplitCidrs(rangeBuilder.BuildInternetIPv4Range([cachedIp]));
        var expectedIpv6 = SplitCidrs(rangeBuilder.BuildInternetIPv6Range([cachedIp]));
        var plan = new GlobalIcmpEnforcementPlan(true, 2, [cachedIp]);

        BuildEnforcer().EnforceGlobalIcmpBlock(plan);

        _wfpGlobalIcmpBlocker.Verify(
            w => w.Apply(
                It.Is<IReadOnlyList<string>>(ranges => ranges.SequenceEqual(expectedIpv4)),
                It.Is<IReadOnlyList<string>>(ranges => ranges.SequenceEqual(expectedIpv6))),
            Times.Once);
    }

    [Fact]
    public void RemoveGlobalIcmpBlock_RemovesGlobalBlock()
    {
        BuildEnforcer().RemoveGlobalIcmpBlock();

        _wfpGlobalIcmpBlocker.Verify(
            w => w.Apply(
                It.Is<IReadOnlyList<string>>(ranges => ranges.Count == 0),
                It.Is<IReadOnlyList<string>>(ranges => ranges.Count == 0)),
            Times.Once);
    }

    private static AccountEntry BlockedAccountWithDomain(string sid, string domain) =>
        new()
        {
            Sid = sid,
            Firewall = new FirewallAccountSettings
            {
                AllowInternet = false,
                Allowlist = [new FirewallAllowlistEntry { Value = domain, IsDomain = true }]
            }
        };

    private static IReadOnlyList<string> SplitCidrs(string ranges)
        => string.IsNullOrEmpty(ranges) ? [] : ranges.Split(',');
}
