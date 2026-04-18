using Moq;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Firewall;
using Xunit;

namespace RunFence.Tests;

public class FirewallComRuleApplierTests
{
    [Fact]
    public void RefreshSingleRule_NullExistingAndNonEmptyAddress_CreatesRule()
    {
        // Arrange
        var ruleManager = new Mock<IFirewallRuleManager>();
        var networkInfo = new Mock<INetworkInterfaceInfoProvider>();
        var log = new Mock<ILoggingService>();

        networkInfo.Setup(n => n.GetDnsServerAddresses()).Returns([]);
        networkInfo.Setup(n => n.GetLocalAddresses()).Returns([]);
        var addressBuilder = new FirewallAddressExclusionBuilder(
            new FirewallAddressRangeBuilder(), networkInfo.Object);
        var comApplier = new FirewallComRuleApplier(ruleManager.Object, addressBuilder, log.Object);

        const string sid = "S-1-5-21-1000-1000-1000-1001";
        const string username = "testuser";

        var currentRules = new List<FirewallRuleInfo>();
        ruleManager.Setup(r => r.GetRulesByGroup("RunFence")).Returns(() => currentRules.ToList());
        ruleManager.Setup(r => r.AddRule(It.IsAny<FirewallRuleInfo>()))
            .Callback<FirewallRuleInfo>(currentRules.Add);

        var settings = new FirewallAccountSettings { AllowInternet = false, AllowLan = true };
        var resolvedDomains = new Dictionary<string, IReadOnlyList<string>>();

        // Act
        var changed = comApplier.RefreshAllowlistRules(sid, username, settings, resolvedDomains);

        // Assert
        Assert.True(changed);
        ruleManager.Verify(r => r.AddRule(It.IsAny<FirewallRuleInfo>()), Times.AtLeast(1));
    }
}
