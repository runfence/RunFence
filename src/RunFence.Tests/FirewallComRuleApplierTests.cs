using Moq;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Firewall;
using Xunit;

namespace RunFence.Tests;

public class FirewallComRuleApplierTests
{
    private const string Sid = "S-1-5-21-1000-1000-1000-1001";
    private const string Username = "testuser";

    private readonly Mock<IFirewallRuleManager> _ruleManager = new();
    private readonly Mock<INetworkInterfaceInfoProvider> _networkInfo = new();
    private readonly Mock<ILoggingService> _log = new();

    private List<FirewallRuleInfo> _currentRules = [];

    private FirewallComRuleApplier BuildApplier()
    {
        _networkInfo.Setup(n => n.GetDnsServerAddresses()).Returns([]);
        _networkInfo.Setup(n => n.GetLocalAddresses()).Returns([]);
        var addressBuilder = new FirewallAddressExclusionBuilder(
            new FirewallAddressRangeBuilder(), _networkInfo.Object);
        return new FirewallComRuleApplier(_ruleManager.Object, addressBuilder, _log.Object);
    }

    private void SetupRuleManagerWithCurrentRules()
    {
        _ruleManager.Setup(r => r.GetRulesByGroup(FirewallConstants.RuleGrouping))
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

    // --- RefreshAllowlistRules: add path ---

    [Fact]
    public void RefreshAllowlistRules_NoExistingRules_BlockingEnabled_CreatesRules()
    {
        // Arrange
        SetupRuleManagerWithCurrentRules();
        var settings = new FirewallAccountSettings { AllowInternet = false, AllowLan = true };

        // Act
        var changed = BuildApplier().RefreshAllowlistRules(Sid, Username, settings,
            new Dictionary<string, IReadOnlyList<string>>());

        // Assert: internet block rules added; changed = true
        Assert.True(changed);
        _ruleManager.Verify(r => r.AddRule(It.IsAny<FirewallRuleInfo>()), Times.AtLeast(1));
    }

    // --- RefreshAllowlistRules: update path ---

    [Fact]
    public void RefreshAllowlistRules_ExistingRuleChanged_CallsUpdateForChangedRule()
    {
        // Arrange: pre-populate with both internet rules using stale addresses so both get updated
        SetupRuleManagerWithCurrentRules();
        var staleIpv4Rule = new FirewallRuleInfo(
            Name: FirewallRuleNames.InternetIPv4RuleName(Username),
            LocalUser: FirewallSddlHelper.BuildSddl(Sid),
            RemoteAddress: "1.2.3.4",
            Direction: 2, Action: 0, Protocol: 256,
            Grouping: FirewallConstants.RuleGrouping,
            Description: "Managed by RunFence");
        var staleIpv6Rule = new FirewallRuleInfo(
            Name: FirewallRuleNames.InternetIPv6RuleName(Username),
            LocalUser: FirewallSddlHelper.BuildSddl(Sid),
            RemoteAddress: "::stale",
            Direction: 2, Action: 0, Protocol: 256,
            Grouping: FirewallConstants.RuleGrouping,
            Description: "Managed by RunFence");
        _currentRules.Add(staleIpv4Rule);
        _currentRules.Add(staleIpv6Rule);

        var settings = new FirewallAccountSettings { AllowInternet = false, AllowLan = true };

        // Act
        var changed = BuildApplier().RefreshAllowlistRules(Sid, Username, settings,
            new Dictionary<string, IReadOnlyList<string>>());

        // Assert: UpdateRule called for both IPv4 and IPv6 (addresses changed from stale to computed)
        Assert.True(changed);
        _ruleManager.Verify(r => r.UpdateRule(
            FirewallRuleNames.InternetIPv4RuleName(Username),
            It.IsAny<FirewallRuleInfo>()), Times.Once);
        _ruleManager.Verify(r => r.UpdateRule(
            FirewallRuleNames.InternetIPv6RuleName(Username),
            It.IsAny<FirewallRuleInfo>()), Times.Once);
        _ruleManager.Verify(r => r.AddRule(It.IsAny<FirewallRuleInfo>()), Times.Never);
    }

    // --- LAN rule application and removal ---

    [Fact]
    public void ApplyLanRules_WhenLanBlocked_AddsLanRules()
    {
        // Arrange
        SetupRuleManagerWithCurrentRules();
        var existing = _currentRules.ToList();
        var settings = new FirewallAccountSettings { AllowInternet = true, AllowLan = false };

        // Act
        var applier = BuildApplier();
        applier.ApplyLanRules(Sid, Username, settings, existing, new Dictionary<string, IReadOnlyList<string>>());

        // Assert: LAN block rules added
        _ruleManager.Verify(r => r.AddRule(
            It.Is<FirewallRuleInfo>(ri => ri.Name.Contains("LAN") && ri.Name.Contains(Username))),
            Times.AtLeast(1));
    }

    [Fact]
    public void ApplyLanRules_WhenLanAllowed_RemovesExistingLanRules()
    {
        // Arrange: pre-populate with LAN rules
        SetupRuleManagerWithCurrentRules();
        var lanIpv4Rule = new FirewallRuleInfo(
            Name: FirewallRuleNames.LanIPv4RuleName(Username),
            LocalUser: FirewallSddlHelper.BuildSddl(Sid),
            RemoteAddress: "192.168.0.0/16",
            Direction: 2, Action: 0, Protocol: 256,
            Grouping: FirewallConstants.RuleGrouping,
            Description: "Managed by RunFence");
        var lanIpv6Rule = new FirewallRuleInfo(
            Name: FirewallRuleNames.LanIPv6RuleName(Username),
            LocalUser: FirewallSddlHelper.BuildSddl(Sid),
            RemoteAddress: "fe80::/10",
            Direction: 2, Action: 0, Protocol: 256,
            Grouping: FirewallConstants.RuleGrouping,
            Description: "Managed by RunFence");
        _currentRules.Add(lanIpv4Rule);
        _currentRules.Add(lanIpv6Rule);

        var existing = _currentRules.ToList();
        var settings = new FirewallAccountSettings { AllowInternet = false, AllowLan = true };

        // Act
        BuildApplier().ApplyLanRules(Sid, Username, settings, existing, new Dictionary<string, IReadOnlyList<string>>());

        // Assert: LAN rules removed
        _ruleManager.Verify(r => r.RemoveRule(FirewallRuleNames.LanIPv4RuleName(Username)), Times.Once);
        _ruleManager.Verify(r => r.RemoveRule(FirewallRuleNames.LanIPv6RuleName(Username)), Times.Once);
    }

    // --- Legacy localhost rule removal ---

    [Fact]
    public void RemoveLocalhostLegacyRules_RemovesLocalhostRulesWhenPresent()
    {
        // Arrange: pre-populate with legacy localhost rules
        SetupRuleManagerWithCurrentRules();
        var localhostIpv4Rule = new FirewallRuleInfo(
            Name: FirewallRuleNames.LocalhostIPv4RuleName(Username),
            LocalUser: FirewallSddlHelper.BuildSddl(Sid),
            RemoteAddress: "127.0.0.1",
            Direction: 2, Action: 0, Protocol: 256,
            Grouping: FirewallConstants.RuleGrouping,
            Description: "Managed by RunFence");
        var localhostIpv6Rule = new FirewallRuleInfo(
            Name: FirewallRuleNames.LocalhostIPv6RuleName(Username),
            LocalUser: FirewallSddlHelper.BuildSddl(Sid),
            RemoteAddress: "::1",
            Direction: 2, Action: 0, Protocol: 256,
            Grouping: FirewallConstants.RuleGrouping,
            Description: "Managed by RunFence");
        _currentRules.Add(localhostIpv4Rule);
        _currentRules.Add(localhostIpv6Rule);

        var existing = _currentRules.ToList();

        // Act
        BuildApplier().RemoveLocalhostLegacyRules(Username, existing);

        // Assert: both legacy localhost rules removed
        _ruleManager.Verify(r => r.RemoveRule(FirewallRuleNames.LocalhostIPv4RuleName(Username)), Times.Once);
        _ruleManager.Verify(r => r.RemoveRule(FirewallRuleNames.LocalhostIPv6RuleName(Username)), Times.Once);
    }

    [Fact]
    public void RemoveLocalhostLegacyRules_NoExistingRules_DoesNothing()
    {
        // Arrange: no existing localhost rules
        SetupRuleManagerWithCurrentRules();
        var existing = new List<FirewallRuleInfo>();

        // Act
        BuildApplier().RemoveLocalhostLegacyRules(Username, existing);

        // Assert: no remove calls
        _ruleManager.Verify(r => r.RemoveRule(It.IsAny<string>()), Times.Never);
    }

    // --- Rollback ---

    [Fact]
    public void RollBackAccountRules_RemovesRulesAddedAfterCapture()
    {
        // Arrange: captured state has one rule; current state has that rule plus a new one added later
        SetupRuleManagerWithCurrentRules();

        var capturedRule = new FirewallRuleInfo(
            Name: FirewallRuleNames.InternetIPv4RuleName(Username),
            LocalUser: FirewallSddlHelper.BuildSddl(Sid),
            RemoteAddress: "0.0.0.0/0",
            Direction: 2, Action: 0, Protocol: 256,
            Grouping: FirewallConstants.RuleGrouping,
            Description: "Managed by RunFence");

        var newRule = new FirewallRuleInfo(
            Name: FirewallRuleNames.LanIPv4RuleName(Username),
            LocalUser: FirewallSddlHelper.BuildSddl(Sid),
            RemoteAddress: "192.168.0.0/16",
            Direction: 2, Action: 0, Protocol: 256,
            Grouping: FirewallConstants.RuleGrouping,
            Description: "Managed by RunFence");

        // Current state: both rules exist (the captured one + the newly-added one)
        _currentRules.Add(capturedRule);
        _currentRules.Add(newRule);

        var capturedRules = new List<FirewallRuleInfo> { capturedRule };
        var applier = BuildApplier();

        // Act
        applier.RollBackAccountRules(Sid, capturedRules);

        // Assert: only the newly-added LAN rule was removed; captured internet rule untouched
        _ruleManager.Verify(r => r.RemoveRule(FirewallRuleNames.LanIPv4RuleName(Username)), Times.Once);
        _ruleManager.Verify(r => r.RemoveRule(FirewallRuleNames.InternetIPv4RuleName(Username)), Times.Never);
        // The captured internet rule was already present with the correct value — no re-add needed
        _ruleManager.Verify(r => r.AddRule(It.IsAny<FirewallRuleInfo>()), Times.Never);
    }

    [Fact]
    public void RollBackAccountRules_RestoresMissingCapturedRules_WhenCurrentIsMissing()
    {
        // Arrange: captured has a rule that no longer exists in the current state
        SetupRuleManagerWithCurrentRules();

        var capturedRule = new FirewallRuleInfo(
            Name: FirewallRuleNames.InternetIPv4RuleName(Username),
            LocalUser: FirewallSddlHelper.BuildSddl(Sid),
            RemoteAddress: "0.0.0.0/0",
            Direction: 2, Action: 0, Protocol: 256,
            Grouping: FirewallConstants.RuleGrouping,
            Description: "Managed by RunFence");

        // Current state: empty (rule was removed between capture and rollback)
        // (currentRules stays empty)

        var capturedRules = new List<FirewallRuleInfo> { capturedRule };
        var applier = BuildApplier();

        // Act
        applier.RollBackAccountRules(Sid, capturedRules);

        // Assert: captured rule re-added
        _ruleManager.Verify(r => r.AddRule(capturedRule), Times.Once);
    }
}
