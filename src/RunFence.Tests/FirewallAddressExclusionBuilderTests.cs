using Moq;
using RunFence.Core.Models;
using RunFence.Firewall;
using Xunit;

namespace RunFence.Tests;

public class FirewallAddressExclusionBuilderTests
{
    private const string Sid = "S-1-5-21-1000-1000-1000-1001";

    private readonly Mock<INetworkInterfaceInfoProvider> _networkInfo = new();

    private FirewallAddressExclusionBuilder BuildBuilder()
    {
        _networkInfo.Setup(n => n.GetDnsServerAddresses()).Returns(["1.1.1.1"]);
        _networkInfo.Setup(n => n.GetLocalAddresses()).Returns(["192.168.1.10"]);
        return new FirewallAddressExclusionBuilder(new FirewallAddressRangeBuilder(), _networkInfo.Object);
    }

    [Fact]
    public void ComputeAllowlistExclusions_UsesCachedDomainsAndReturnsPendingMissingDomains()
    {
        var settings = new FirewallAccountSettings
        {
            AllowLocalhost = true,
            Allowlist =
            [
                new FirewallAllowlistEntry { Value = "8.8.8.8", IsDomain = false },
                new FirewallAllowlistEntry { Value = "cached.example", IsDomain = true },
                new FirewallAllowlistEntry { Value = "missing.example", IsDomain = true }
            ]
        };
        var cache = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["cached.example"] = ["2.2.2.2"]
        };
        var builder = BuildBuilder();

        var result = builder.ComputeAllowlistExclusions(Sid, settings, cache);

        Assert.Contains("1.1.1.1", result.Exclusions);
        Assert.Contains("192.168.1.10", result.Exclusions);
        Assert.Contains("8.8.8.8", result.Exclusions);
        Assert.Contains("2.2.2.2", result.Exclusions);
        Assert.DoesNotContain("missing.example", result.Exclusions);
        var pending = Assert.Single(result.PendingDomains);
        Assert.Equal(Sid, pending.Sid);
        Assert.Equal("missing.example", pending.Domain);
    }

    [Fact]
    public void ComputeCommonIcmpExclusions_CollectsPendingDomainsAfterIntersectionBecomesEmpty()
    {
        _networkInfo.Setup(n => n.GetDnsServerAddresses()).Returns([]);
        _networkInfo.Setup(n => n.GetLocalAddresses()).Returns([]);
        var builder = new FirewallAddressExclusionBuilder(new FirewallAddressRangeBuilder(), _networkInfo.Object);
        var firstAccount = new AccountEntry
        {
            Sid = "S-1-5-21-1",
            Firewall = new FirewallAccountSettings
            {
                AllowLocalhost = false,
                Allowlist =
                [
                    new FirewallAllowlistEntry { Value = "10.10.10.10", IsDomain = false },
                    new FirewallAllowlistEntry { Value = "first.example", IsDomain = true }
                ]
            }
        };
        var secondAccount = new AccountEntry
        {
            Sid = "S-1-5-21-2",
            Firewall = new FirewallAccountSettings
            {
                AllowLocalhost = false,
                Allowlist =
                [
                    new FirewallAllowlistEntry { Value = "20.20.20.20", IsDomain = false },
                    new FirewallAllowlistEntry { Value = "second.example", IsDomain = true }
                ]
            }
        };

        var result = builder.ComputeCommonIcmpExclusions([firstAccount, secondAccount], new Dictionary<string, IReadOnlyList<string>>());

        Assert.Empty(result.CommonExclusions);
        Assert.Equal(2, result.PendingDomains.Count);
        Assert.Contains(result.PendingDomains, p => p.Sid == firstAccount.Sid && p.Domain == "first.example");
        Assert.Contains(result.PendingDomains, p => p.Sid == secondAccount.Sid && p.Domain == "second.example");
    }

    [Fact]
    public void ComputeCommonIcmpExclusions_DoesNotIncludeLocalAddresses()
    {
        _networkInfo.Setup(n => n.GetDnsServerAddresses()).Returns([]);
        _networkInfo.Setup(n => n.GetLocalAddresses()).Returns(["192.168.1.10"]);
        var builder = new FirewallAddressExclusionBuilder(new FirewallAddressRangeBuilder(), _networkInfo.Object);
        var firstAccount = BlockedAccount("S-1-5-21-1");
        var secondAccount = BlockedAccount("S-1-5-21-2");

        var result = builder.ComputeCommonIcmpExclusions(
            [firstAccount, secondAccount],
            new Dictionary<string, IReadOnlyList<string>>());

        Assert.DoesNotContain("192.168.1.10", result.CommonExclusions);
    }

    private static AccountEntry BlockedAccount(string sid) =>
        new()
        {
            Sid = sid,
            Firewall = new FirewallAccountSettings
            {
                AllowInternet = false,
                AllowLocalhost = true,
                Allowlist = [new FirewallAllowlistEntry { Value = "203.0.113.10", IsDomain = false }]
            }
        };
}
