using Moq;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Firewall;
using RunFence.Firewall.Wfp;
using Xunit;

namespace RunFence.Tests;

public class FirewallAccountRuleApplierTests
{
    private const string Sid = "S-1-5-21-1000-1000-1000-1001";
    private const string Username = "alice";

    private readonly Mock<ILoggingService> _log = new();
    private readonly Mock<IFirewallRuleManager> _ruleManager = new();
    private readonly Mock<INetworkInterfaceInfoProvider> _networkInfo = new();
    private readonly Mock<IWfpLocalhostBlocker> _wfpBlocker = new();
    private readonly Mock<IWfpIcmpBlocker> _wfpIcmpBlocker = new();

    private FirewallAccountRuleApplier BuildApplier()
    {
        _networkInfo.Setup(n => n.GetDnsServerAddresses()).Returns([]);
        _networkInfo.Setup(n => n.GetLocalAddresses()).Returns([]);
        var addressBuilder = new FirewallAddressExclusionBuilder(new FirewallAddressRangeBuilder(), _networkInfo.Object);
        var comApplier = new FirewallComRuleApplier(_ruleManager.Object, addressBuilder, _log.Object);
        var wfpApplier = new FirewallWfpRuleApplier(_wfpBlocker.Object, _wfpIcmpBlocker.Object, _log.Object);
        return new FirewallAccountRuleApplier(comApplier, wfpApplier);
    }

    [Fact]
    public void ApplyFirewallRules_SuccessPath_AddsBlockRulesAndAppliesWfp()
    {
        var currentRules = new List<FirewallRuleInfo>();
        _ruleManager.Setup(r => r.GetRulesByGroup("RunFence")).Returns(() => currentRules.ToList());
        _ruleManager.Setup(r => r.AddRule(It.IsAny<FirewallRuleInfo>()))
            .Callback<FirewallRuleInfo>(currentRules.Add);

        BuildApplier().ApplyFirewallRules(
            Sid,
            Username,
            new FirewallAccountSettings { AllowInternet = false },
            previousSettings: null,
            resolvedDomainsCache: new Dictionary<string, IReadOnlyList<string>>());

        // Block rules were added (at least IPv4 and IPv6 internet rules)
        Assert.NotEmpty(currentRules);
        Assert.All(currentRules, r => Assert.Equal("RunFence", r.Grouping));
        Assert.All(currentRules, r => Assert.Contains(Username, r.Name));

        // WFP localhost blocker applied with the account's SID (AllowLocalhost=true → apply=false because apply = !AllowLocalhost)
        _wfpBlocker.Verify(w => w.Apply(
            Sid,
            false,
            It.IsAny<IReadOnlyList<string>>()), Times.Once);

        // WFP ICMP blocker applied alongside internet block
        _wfpIcmpBlocker.Verify(w => w.Apply(Sid, true), Times.Once);
    }

    [Fact]
    public void ApplyFirewallRules_WhenAddFails_RemovesNewRulesAndRestoresDefaultWfpState()
    {
        var currentRules = new List<FirewallRuleInfo>();
        _ruleManager.Setup(r => r.GetRulesByGroup("RunFence")).Returns(() => currentRules.ToList());
        var addCount = 0;
        _ruleManager.Setup(r => r.AddRule(It.IsAny<FirewallRuleInfo>()))
            .Callback<FirewallRuleInfo>(rule =>
            {
                if (addCount++ == 0)
                {
                    currentRules.Add(rule);
                    return;
                }

                throw new InvalidOperationException("add failed");
            });
        _ruleManager.Setup(r => r.RemoveRule(It.IsAny<string>()))
            .Callback<string>(name => currentRules.RemoveAll(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase)));

        var ex = Assert.Throws<FirewallApplyException>(() => BuildApplier().ApplyFirewallRules(
            Sid,
            Username,
            new FirewallAccountSettings { AllowInternet = false },
            previousSettings: null,
            resolvedDomainsCache: new Dictionary<string, IReadOnlyList<string>>()));

        Assert.Equal(FirewallApplyPhase.AccountRules, ex.Phase);
        Assert.IsType<InvalidOperationException>(ex.InnerException);
        Assert.Empty(currentRules);
        _wfpBlocker.Verify(w => w.Apply(
            Sid,
            false,
            It.Is<IReadOnlyList<string>>(ports => ports.SequenceEqual(new[] { "53", "49152-65535" }))), Times.Once);
        _wfpIcmpBlocker.Verify(w => w.Apply(Sid, false), Times.Once);
    }

    [Fact]
    public void ApplyFirewallRules_WhenWfpFails_RestoresFullCapturedFirewallRules()
    {
        var capturedRule = MakeRule(
            $"RunFence Block Internet IPv4 ({Username})",
            remoteAddress: "1.2.3.4",
            description: "custom captured description");
        var currentRules = new List<FirewallRuleInfo> { capturedRule };
        _ruleManager.Setup(r => r.GetRulesByGroup("RunFence")).Returns(() => currentRules.ToList());
        _ruleManager.Setup(r => r.UpdateRule(It.IsAny<string>(), It.IsAny<FirewallRuleInfo>()))
            .Callback<string, FirewallRuleInfo>((existingName, replacement) =>
            {
                var index = currentRules.FindIndex(r => string.Equals(r.Name, existingName, StringComparison.OrdinalIgnoreCase));
                if (index >= 0)
                    currentRules[index] = replacement;
            });
        _ruleManager.Setup(r => r.AddRule(It.IsAny<FirewallRuleInfo>()))
            .Callback<FirewallRuleInfo>(currentRules.Add);
        _ruleManager.Setup(r => r.RemoveRule(It.IsAny<string>()))
            .Callback<string>(name => currentRules.RemoveAll(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase)));
        _wfpIcmpBlocker.Setup(w => w.Apply(Sid, true)).Throws(new InvalidOperationException("icmp failed"));

        var ex = Assert.Throws<FirewallApplyException>(() => BuildApplier().ApplyFirewallRules(
            Sid,
            Username,
            new FirewallAccountSettings { AllowInternet = false },
            previousSettings: new FirewallAccountSettings { AllowInternet = true, AllowLocalhost = false },
            resolvedDomainsCache: new Dictionary<string, IReadOnlyList<string>>()));

        Assert.Equal(FirewallApplyPhase.AccountRules, ex.Phase);
        Assert.IsType<InvalidOperationException>(ex.InnerException);
        var restoredRule = Assert.Single(currentRules);
        Assert.Equal(capturedRule, restoredRule);
        _wfpBlocker.Verify(w => w.Apply(
            Sid,
            true,
            It.Is<IReadOnlyList<string>>(ports => ports.SequenceEqual(new[] { "53", "49152-65535" }))), Times.Once);
        _wfpIcmpBlocker.Verify(w => w.Apply(Sid, false), Times.Once);
    }

    private static FirewallRuleInfo MakeRule(
        string name,
        string remoteAddress,
        string description = "Managed by RunFence") =>
        new(
            Name: name,
            LocalUser: $"D:(A;;CC;;;{Sid})",
            RemoteAddress: remoteAddress,
            Direction: 2,
            Action: 0,
            Protocol: 256,
            Grouping: "RunFence",
            Description: description);
}
