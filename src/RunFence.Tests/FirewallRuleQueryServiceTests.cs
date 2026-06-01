using Moq;
using RunFence.Core.Models;
using RunFence.Firewall;
using Xunit;

namespace RunFence.Tests;

public class FirewallRuleQueryServiceTests
{
    private const string Sid = "S-1-5-21-1000-1000-1000-1001";

    private readonly Mock<IFirewallRuleManager> _ruleManager = new();

    private FirewallRuleQueryService BuildService() => new(_ruleManager.Object);

    [Fact]
    public void GetExistingRulesBySid_ReturnsOnlyMatchingSidRules()
    {
        var rules = new List<FirewallRuleInfo>
        {
            new(
                Name: "RunFence Block Internet IPv4 (user1)",
                LocalUser: FirewallSddlHelper.BuildSddl(Sid),
                RemoteAddress: "0.0.0.0/0",
                Direction: 2,
                Action: 0,
                Protocol: 256,
                Grouping: FirewallConstants.RuleGrouping,
                Description: "Managed by RunFence"),
            new(
                Name: "RunFence Block Internet IPv6 (user1)",
                LocalUser: FirewallSddlHelper.BuildSddl("S-1-5-21-1000-1000-1000-2002"),
                RemoteAddress: "::/0",
                Direction: 2,
                Action: 0,
                Protocol: 256,
                Grouping: FirewallConstants.RuleGrouping,
                Description: "Managed by RunFence")
        };
        _ruleManager
            .Setup(r => r.GetRulesByGroup(FirewallConstants.RuleGrouping))
            .Returns(rules);

        var result = BuildService().GetExistingRulesBySid(Sid);

        var single = Assert.Single(result);
        Assert.Equal(rules[0], single);
    }

    [Fact]
    public void GetExistingRulesBySid_SidMatchIsCaseInsensitive()
    {
        var rules = new List<FirewallRuleInfo>
        {
            new(
                Name: "RunFence Block Internet IPv4 (user1)",
                LocalUser: FirewallSddlHelper.BuildSddl(Sid.ToLowerInvariant()),
                RemoteAddress: "0.0.0.0/0",
                Direction: 2,
                Action: 0,
                Protocol: 256,
                Grouping: FirewallConstants.RuleGrouping,
                Description: "Managed by RunFence")
        };
        _ruleManager
            .Setup(r => r.GetRulesByGroup(FirewallConstants.RuleGrouping))
            .Returns(rules);

        var result = BuildService().GetExistingRulesBySid(Sid.ToUpperInvariant());

        Assert.Single(result);
    }

    [Fact]
    public void GetExistingRulesBySid_NoMatchingSidReturnsEmpty()
    {
        var rules = new List<FirewallRuleInfo>
        {
            new(
                Name: "RunFence Block Internet IPv4 (user1)",
                LocalUser: FirewallSddlHelper.BuildSddl("S-1-5-21-1000-1000-1000-2002"),
                RemoteAddress: "0.0.0.0/0",
                Direction: 2,
                Action: 0,
                Protocol: 256,
                Grouping: FirewallConstants.RuleGrouping,
                Description: "Managed by RunFence")
        };
        _ruleManager
            .Setup(r => r.GetRulesByGroup(FirewallConstants.RuleGrouping))
            .Returns(rules);

        var result = BuildService().GetExistingRulesBySid(Sid);

        Assert.Empty(result);
    }
}
