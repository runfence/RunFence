using Moq;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Firewall;
using Xunit;

namespace RunFence.Tests;

public class FirewallRuleRollbackCoordinatorTests
{
    private const string Sid = "S-1-5-21-1000-1000-1000-1001";
    private const string Username = "testuser";

    private readonly Mock<IFirewallRuleQueryService> _ruleQueryService = new();
    private readonly Mock<IFirewallRuleManager> _ruleManager = new();
    private readonly Mock<ILoggingService> _log = new();
    private List<FirewallRuleInfo> _currentRules = [];

    private FirewallRuleRollbackCoordinator BuildCoordinator() =>
        new(_ruleQueryService.Object, _ruleManager.Object, _log.Object);

    private void SetupCurrentRules()
    {
        _ruleQueryService.Setup(r => r.GetExistingRulesBySid(Sid))
            .Returns(() => _currentRules.ToList());
        _ruleManager.Setup(r => r.AddRule(It.IsAny<FirewallRuleInfo>()))
            .Callback<FirewallRuleInfo>(_currentRules.Add);
        _ruleManager.Setup(r => r.RemoveRule(It.IsAny<string>()))
            .Callback<string>(name => _currentRules.RemoveAll(r =>
                string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase)));
        _ruleManager.Setup(r => r.UpdateRule(It.IsAny<string>(), It.IsAny<FirewallRuleInfo>()))
            .Callback<string, FirewallRuleInfo>((name, updated) =>
            {
                _currentRules.RemoveAll(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));
                _currentRules.Add(updated);
            });
    }

    [Fact]
    public void RestoreWindowsFirewallRules_RemovesRulesAddedAfterCapture()
    {
        SetupCurrentRules();

        var capturedRule = new FirewallRuleInfo(
            Name: FirewallRuleNames.InternetIPv4RuleName(Username),
            LocalUser: FirewallSddlHelper.BuildSddl(Sid),
            RemoteAddress: "0.0.0.0/0",
            Direction: 2,
            Action: 0,
            Protocol: 256,
            Grouping: FirewallConstants.RuleGrouping,
            Description: "Managed by RunFence");

        var newRule = new FirewallRuleInfo(
            Name: FirewallRuleNames.LanIPv4RuleName(Username),
            LocalUser: FirewallSddlHelper.BuildSddl(Sid),
            RemoteAddress: "192.168.0.0/16",
            Direction: 2,
            Action: 0,
            Protocol: 256,
            Grouping: FirewallConstants.RuleGrouping,
            Description: "Managed by RunFence");

        _currentRules.Add(capturedRule);
        _currentRules.Add(newRule);

        BuildCoordinator().RestoreWindowsFirewallRules(Sid, [capturedRule]);

        _ruleQueryService.Verify(r => r.GetExistingRulesBySid(Sid), Times.Exactly(2));
        _ruleManager.Verify(r => r.RemoveRule(FirewallRuleNames.LanIPv4RuleName(Username)), Times.Once);
        _ruleManager.Verify(r => r.RemoveRule(FirewallRuleNames.InternetIPv4RuleName(Username)), Times.Never);
        _ruleManager.Verify(r => r.AddRule(It.IsAny<FirewallRuleInfo>()), Times.Never);
    }

    [Fact]
    public void RestoreWindowsFirewallRules_RestoresMissingCapturedRules_WhenCurrentIsMissing()
    {
        SetupCurrentRules();

        var capturedRule = new FirewallRuleInfo(
            Name: FirewallRuleNames.InternetIPv4RuleName(Username),
            LocalUser: FirewallSddlHelper.BuildSddl(Sid),
            RemoteAddress: "0.0.0.0/0",
            Direction: 2,
            Action: 0,
            Protocol: 256,
            Grouping: FirewallConstants.RuleGrouping,
            Description: "Managed by RunFence");

        BuildCoordinator().RestoreWindowsFirewallRules(Sid, [capturedRule]);

        _ruleQueryService.Verify(r => r.GetExistingRulesBySid(Sid), Times.Exactly(2));
        _ruleManager.Verify(r => r.AddRule(capturedRule), Times.Once);
    }

    [Fact]
    public void RestoreWindowsFirewallRules_UpdatesChangedCapturedRule()
    {
        SetupCurrentRules();

        var capturedRule = new FirewallRuleInfo(
            Name: FirewallRuleNames.InternetIPv4RuleName(Username),
            LocalUser: FirewallSddlHelper.BuildSddl(Sid),
            RemoteAddress: "0.0.0.0/0",
            Direction: 2,
            Action: 0,
            Protocol: 256,
            Grouping: FirewallConstants.RuleGrouping,
            Description: "Managed by RunFence");

        var changedCurrentRule = capturedRule with
        {
            RemoteAddress = "192.168.1.0/24",
            Direction = 1,
        };

        _currentRules.Add(changedCurrentRule);

        BuildCoordinator().RestoreWindowsFirewallRules(Sid, [capturedRule]);

        _ruleQueryService.Verify(r => r.GetExistingRulesBySid(Sid), Times.Exactly(2));
        _ruleManager.Verify(r => r.UpdateRule(changedCurrentRule.Name, capturedRule), Times.Once);
        _ruleManager.Verify(r => r.AddRule(It.IsAny<FirewallRuleInfo>()), Times.Never);
        _ruleManager.Verify(r => r.RemoveRule(It.IsAny<string>()), Times.Never);
    }
}
