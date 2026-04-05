using System.ComponentModel;
using System.Runtime.InteropServices;
using Moq;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Firewall;
using RunFence.Firewall.Wfp;
using Xunit;

namespace RunFence.Tests;

public class FirewallServiceTests
{
    private const string Sid = "S-1-5-21-1000-1000-1000-1001";
    private const string Username = "alice";

    private readonly Mock<IFirewallRuleManager> _ruleManager = new();
    private readonly Mock<IDnsResolver> _dnsResolver = new();
    private readonly Mock<INetworkInterfaceInfoProvider> _networkInfo = new();
    private readonly Mock<ILoggingService> _log = new();
    private readonly Mock<IWfpLocalhostBlocker> _wfpBlocker = new();

    private FirewallService BuildService() =>
        new(_log.Object, new FirewallAddressRangeBuilder(), _ruleManager.Object, _dnsResolver.Object, _networkInfo.Object, _wfpBlocker.Object);

    private static FirewallAccountSettings BlockInternetOnly() =>
        new() { AllowInternet = false, AllowLocalhost = true, AllowLan = true };

    private static FirewallAccountSettings BlockAll() =>
        new() { AllowInternet = false, AllowLocalhost = false, AllowLan = false };

    private static FirewallAccountSettings AllowAll() =>
        new() { AllowInternet = true, AllowLocalhost = true, AllowLan = true };

    private FirewallRuleInfo MakeRule(string name, string sid = Sid) =>
        new(Name: name,
            LocalUser: $"D:(A;;CC;;;{sid})",
            RemoteAddress: "1.2.3.4",
            Direction: 2,
            Action: 0,
            Protocol: 256,
            Grouping: "RunFence",
            Description: "Managed by RunFence");

    /// <summary>Sets up the most common baseline: no existing rules, no DNS servers, no local addresses.</summary>
    private void SetupNoExistingRulesAndNoDnsServers()
    {
        _ruleManager.Setup(r => r.GetRulesByGroup("RunFence")).Returns([]);
        _networkInfo.Setup(n => n.GetDnsServerAddresses()).Returns([]);
        _networkInfo.Setup(n => n.GetLocalAddresses()).Returns([]);
    }

    [Fact]
    public void ApplyFirewallRules_BlockInternetOnly_AddsOnlyInternetRules()
    {
        // Arrange
        SetupNoExistingRulesAndNoDnsServers();
        var addedRules = new List<FirewallRuleInfo>();
        _ruleManager.Setup(r => r.AddRule(It.IsAny<FirewallRuleInfo>()))
            .Callback<FirewallRuleInfo>(addedRules.Add);
        var service = BuildService();

        // Act
        service.ApplyFirewallRules(Sid, Username, BlockInternetOnly());

        // Assert — only IPv4 + IPv6 internet block rules added
        Assert.Equal(2, addedRules.Count);
        Assert.All(addedRules, r => Assert.Contains("Internet", r.Name));
        Assert.All(addedRules, r => Assert.Contains(Username, r.Name));
        Assert.All(addedRules, r => Assert.Contains(Sid, r.LocalUser));
        Assert.All(addedRules, r => Assert.Equal($"D:(A;;CC;;;{Sid})", r.LocalUser));
        _ruleManager.Verify(r => r.RemoveRule(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void ApplyFirewallRules_BlockAll_Adds4INetFwRulesAndBlocksLocalhostViaWfp()
    {
        // Arrange
        SetupNoExistingRulesAndNoDnsServers();
        var addedRules = new List<FirewallRuleInfo>();
        _ruleManager.Setup(r => r.AddRule(It.IsAny<FirewallRuleInfo>()))
            .Callback<FirewallRuleInfo>(addedRules.Add);
        var service = BuildService();

        // Act
        service.ApplyFirewallRules(Sid, Username, BlockAll());

        // Assert — 2 internet + 2 LAN via INetFwRule (localhost uses WFP, not INetFwRule)
        Assert.Equal(4, addedRules.Count);
        Assert.Equal(2, addedRules.Count(r => r.Name.Contains("Internet")));
        Assert.Equal(2, addedRules.Count(r => r.Name.Contains("LAN")));
        Assert.DoesNotContain(addedRules, r => r.Name.Contains("Localhost"));

        // Localhost blocking goes through WFP blocker
        _wfpBlocker.Verify(w => w.Apply(Sid, true), Times.Once);
    }

    [Fact]
    public void ApplyFirewallRules_AllowLocalhost_DisablesWfpLocalhostBlock()
    {
        // Arrange
        SetupNoExistingRulesAndNoDnsServers();
        var service = BuildService();

        // Act — localhost allowed (default)
        service.ApplyFirewallRules(Sid, Username, BlockInternetOnly());

        // Assert — WFP blocker called with block=false
        _wfpBlocker.Verify(w => w.Apply(Sid, false), Times.Once);
    }

    [Fact]
    public void ApplyFirewallRules_AllAllowed_NeitherAddsNorRemovesRules()
    {
        // Arrange
        SetupNoExistingRulesAndNoDnsServers();
        var service = BuildService();

        // Act
        service.ApplyFirewallRules(Sid, Username, AllowAll());

        // Assert
        _ruleManager.Verify(r => r.AddRule(It.IsAny<FirewallRuleInfo>()), Times.Never);
        _ruleManager.Verify(r => r.RemoveRule(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void ApplyFirewallRules_ExistingRuleWithSameSid_UpdatesInsteadOfAdd()
    {
        // Arrange — existing internet IPv4 rule for this SID, but stale remote address
        var existingIpv4 = MakeRule($"RunFence Block Internet IPv4 ({Username})");
        var existingIpv6 = MakeRule($"RunFence Block Internet IPv6 ({Username})");
        _ruleManager.Setup(r => r.GetRulesByGroup("RunFence")).Returns([existingIpv4, existingIpv6]);
        _networkInfo.Setup(n => n.GetDnsServerAddresses()).Returns([]);
        _networkInfo.Setup(n => n.GetLocalAddresses()).Returns([]);
        var service = BuildService();

        // Act — existing rule's RemoteAddress ("1.2.3.4") differs from computed range
        service.ApplyFirewallRules(Sid, Username, BlockInternetOnly());

        // Assert — both existing internet rules updated in-place, nothing added
        _ruleManager.Verify(r => r.UpdateRule(It.IsAny<string>(), It.IsAny<FirewallRuleInfo>()), Times.Exactly(2));
        _ruleManager.Verify(r => r.AddRule(It.IsAny<FirewallRuleInfo>()), Times.Never);
    }

    [Fact]
    public void ApplyFirewallRules_RestrictionLifted_RemovesExistingRules()
    {
        // Arrange — existing internet block rules for this SID
        var existingIpv4 = MakeRule($"RunFence Block Internet IPv4 ({Username})");
        var existingIpv6 = MakeRule($"RunFence Block Internet IPv6 ({Username})");
        _ruleManager.Setup(r => r.GetRulesByGroup("RunFence")).Returns([existingIpv4, existingIpv6]);
        _networkInfo.Setup(n => n.GetDnsServerAddresses()).Returns([]); // no DNS needed for AllowAll
        var service = BuildService();

        // Act — switch to allow internet (restriction lifted)
        service.ApplyFirewallRules(Sid, Username, AllowAll());

        // Assert — removed
        _ruleManager.Verify(r => r.RemoveRule(existingIpv4.Name), Times.Once);
        _ruleManager.Verify(r => r.RemoveRule(existingIpv6.Name), Times.Once);
        _ruleManager.Verify(r => r.AddRule(It.IsAny<FirewallRuleInfo>()), Times.Never);
    }

    [Fact]
    public void RemoveAllRules_RemovesOnlyRulesForTargetSid()
    {
        // Arrange — 2 rules for our SID, 2 for a different SID
        const string otherSid = "S-1-5-21-9999-9999-9999-9999";
        var ours1 = MakeRule($"RunFence Block Internet IPv4 ({Username})");
        var ours2 = MakeRule($"RunFence Block Internet IPv6 ({Username})");
        var theirs1 = MakeRule("RunFence Block Internet IPv4 (bob)", otherSid);
        var theirs2 = MakeRule("RunFence Block Internet IPv6 (bob)", otherSid);
        _ruleManager.Setup(r => r.GetRulesByGroup("RunFence"))
            .Returns([ours1, ours2, theirs1, theirs2]);
        var service = BuildService();

        // Act
        service.RemoveAllRules(Sid);

        // Assert — only our 2 rules removed
        _ruleManager.Verify(r => r.RemoveRule(ours1.Name), Times.Once);
        _ruleManager.Verify(r => r.RemoveRule(ours2.Name), Times.Once);
        _ruleManager.Verify(r => r.RemoveRule(theirs1.Name), Times.Never);
        _ruleManager.Verify(r => r.RemoveRule(theirs2.Name), Times.Never);
    }

    [Fact]
    public void EnforceAll_AppliesToKnownSidAndRemovesOrphan()
    {
        // Arrange — database has one SID entry; rule list has a rule for that SID plus an orphan
        const string orphanSid = "S-1-5-21-0-0-0-9999";
        var database = new AppDatabase
        {
            SidNames =
            {
                [Sid] = Username
            }
        };
        database.GetOrCreateAccount(Sid).Firewall = BlockInternetOnly();

        var knownRule = MakeRule($"RunFence Block Internet IPv4 ({Username})");
        var orphanRule = MakeRule("RunFence Block Internet IPv4 (orphan)", orphanSid);
        _ruleManager.Setup(r => r.GetRulesByGroup("RunFence"))
            .Returns([knownRule, orphanRule]);
        _networkInfo.Setup(n => n.GetDnsServerAddresses()).Returns([]);
        _networkInfo.Setup(n => n.GetLocalAddresses()).Returns([]);

        var service = BuildService();

        // Act
        service.EnforceAll(database);

        // Assert — orphan rule removed
        _ruleManager.Verify(r => r.RemoveRule(orphanRule.Name), Times.Once);
        _ruleManager.Verify(r => r.RemoveRule(knownRule.Name), Times.Never);

        // Verify rules were applied for the known SID (update existing IPv4, add new IPv6)
        _ruleManager.Verify(r => r.UpdateRule(
            It.IsAny<string>(),
            It.Is<FirewallRuleInfo>(info => info.Name.Contains(Username))), Times.Once);
        _ruleManager.Verify(r => r.AddRule(
            It.Is<FirewallRuleInfo>(info => info.Name.Contains(Username))), Times.AtLeastOnce);
    }

    [Fact]
    public void ApplyFirewallRules_ComExceptionFromGetRules_DoesNotThrow()
    {
        // Arrange
        _ruleManager.Setup(r => r.GetRulesByGroup(It.IsAny<string>()))
            .Throws(new COMException("Firewall COM error", -2147467261));
        var service = BuildService();

        // Act & Assert — must not throw
        var ex = Record.Exception(() => service.ApplyFirewallRules(Sid, Username, BlockInternetOnly()));
        Assert.Null(ex);
    }

    [Fact]
    public void ApplyFirewallRules_AllowlistNonEmpty_DnsServerExclusionNarrowsInternetRange()
    {
        // Arrange — non-empty allowlist → DNS server 8.8.8.8 must be excluded from the blocking range.
        // The range builder performs CIDR subtraction, so the DNS IP won't appear literally in the
        // result string — instead the resulting range is smaller than the full internet range.
        const string dnsServer = "8.8.8.8";
        _ruleManager.Setup(r => r.GetRulesByGroup("RunFence")).Returns([]);
        _networkInfo.Setup(n => n.GetDnsServerAddresses()).Returns([dnsServer]);
        _dnsResolver
            .Setup(d => d.ResolveAsync(It.IsAny<string>()))
            .ReturnsAsync(["203.0.113.1"]);

        var settingsWithDomain = new FirewallAccountSettings
        {
            AllowInternet = false,
            AllowLocalhost = true,
            AllowLan = true,
            Allowlist = [new FirewallAllowlistEntry { Value = "example.com", IsDomain = true }]
        };
        var settingsEmpty = new FirewallAccountSettings
        {
            AllowInternet = false,
            AllowLocalhost = true,
            AllowLan = true,
            Allowlist = []
        };

        _networkInfo.Setup(n => n.GetLocalAddresses()).Returns([]);
        var rulesWithDomain = new List<FirewallRuleInfo>();
        var rulesEmpty = new List<FirewallRuleInfo>();

        // First call: with domain allowlist (DNS servers + resolved IP excluded)
        _ruleManager.Setup(r => r.AddRule(It.IsAny<FirewallRuleInfo>()))
            .Callback<FirewallRuleInfo>(r => rulesWithDomain.Add(r));
        BuildService().ApplyFirewallRules(Sid, Username, settingsWithDomain);

        // Second call: with empty allowlist (no DNS server exclusion)
        _ruleManager.Setup(r => r.AddRule(It.IsAny<FirewallRuleInfo>()))
            .Callback<FirewallRuleInfo>(r => rulesEmpty.Add(r));
        BuildService().ApplyFirewallRules(Sid, Username, settingsEmpty);

        // Assert — the IPv4 internet rule with a domain allowlist produces a narrower range
        // (more CIDRs) than the one without, because DNS servers + resolved IP are carved out.
        var ipv4WithDomain = rulesWithDomain.First(r => r.Name.Contains("Internet") && r.Name.Contains("IPv4"));
        var ipv4Empty = rulesEmpty.First(r => r.Name.Contains("Internet") && r.Name.Contains("IPv4"));
        Assert.NotEqual(ipv4WithDomain.RemoteAddress, ipv4Empty.RemoteAddress);
    }

    [Fact]
    public void RefreshAllowlistRules_AllowlistIpChanged_UpdatesInternetRulesInPlace()
    {
        // Arrange — existing internet rules have RemoteAddress "1.2.3.4" (a plain IP, not a CIDR range).
        // After calling RefreshAllowlistRules the service recomputes the full internet CIDR set
        // (minus the allowlisted IP), which is a different string → UpdateRule is called.
        var existingIpv4 = MakeRule($"RunFence Block Internet IPv4 ({Username})") with
        {
            RemoteAddress = "1.2.3.4"
        };
        var existingIpv6 = MakeRule($"RunFence Block Internet IPv6 ({Username})") with
        {
            RemoteAddress = "1.2.3.4"
        };
        _ruleManager.Setup(r => r.GetRulesByGroup("RunFence")).Returns([existingIpv4, existingIpv6]);
        _networkInfo.Setup(n => n.GetDnsServerAddresses()).Returns([]);

        _networkInfo.Setup(n => n.GetLocalAddresses()).Returns([]);
        // Allowlist contains one IP entry (no domain resolution needed)
        var settings = new FirewallAccountSettings
        {
            AllowInternet = false,
            Allowlist = [new FirewallAllowlistEntry { Value = "203.0.113.100", IsDomain = false }]
        };
        var service = BuildService();

        // Act
        var changed = service.RefreshAllowlistRules(Sid, Username, settings);

        // Assert — both internet rules updated (real CIDR range ≠ "1.2.3.4"), returns true
        Assert.True(changed);
        _ruleManager.Verify(r => r.UpdateRule(It.IsAny<string>(), It.IsAny<FirewallRuleInfo>()),
            Times.Exactly(2));
        _ruleManager.Verify(r => r.AddRule(It.IsAny<FirewallRuleInfo>()), Times.Never);
    }

    [Fact]
    public void RefreshAllowlistRules_AllowInternetTrue_ReturnsFalseWithoutQueryingRules()
    {
        // Arrange — AllowInternet=true means there are no internet block rules to refresh
        var settings = new FirewallAccountSettings { AllowInternet = true };
        var service = BuildService();

        // Act
        var changed = service.RefreshAllowlistRules(Sid, Username, settings);

        // Assert
        Assert.False(changed);
        _ruleManager.Verify(r => r.GetRulesByGroup(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void EnforceAll_FirewallUnavailable_LogsWarningAndDoesNotThrow()
    {
        // Arrange — GetRulesByGroup throws (firewall service unavailable)
        _ruleManager.Setup(r => r.GetRulesByGroup(It.IsAny<string>()))
            .Throws(new COMException("Firewall service unavailable", unchecked((int)0x80070422)));

        var database = new AppDatabase();
        database.GetOrCreateAccount(Sid).Firewall = new FirewallAccountSettings { AllowInternet = false };
        var service = BuildService();

        // Act — should not throw
        var ex = Record.Exception(() => service.EnforceAll(database));

        // Assert
        Assert.Null(ex);
        _log.Verify(l => l.Warn(It.Is<string>(s => s.Contains("Firewall"))), Times.Once);
        _ruleManager.Verify(r => r.AddRule(It.IsAny<FirewallRuleInfo>()), Times.Never);
    }

    [Fact]
    public void RefreshLocalAddressRules_LocalAddressChanged_UpdatesRuleInPlace()
    {
        // Arrange — existing local address IPv4 rule has stale RemoteAddress; after interfaces change
        // the new IP set should update the rule in-place (UpdateRule, not RemoveRule+AddRule).
        // No IPv6 rule is present (a rule with empty RemoteAddress would never be created by EnsureRule).
        const string OldIp = "192.168.1.5";
        const string newIp = "192.168.1.99";
        var existingIpv4 = MakeRule($"RunFence Block Local Addresses IPv4 ({Username})") with
        {
            RemoteAddress = OldIp
        };
        _ruleManager.Setup(r => r.GetRulesByGroup("RunFence")).Returns([existingIpv4]);
        _networkInfo.Setup(n => n.GetLocalAddresses()).Returns([newIp]);
        var service = BuildService();

        // Act
        var changed = service.RefreshLocalAddressRules(Sid, Username,
            new FirewallAccountSettings { AllowLocalhost = false });

        // Assert — IPv4 rule updated in-place, not removed and re-added
        Assert.True(changed);
        _ruleManager.Verify(r => r.UpdateRule(existingIpv4.Name, It.Is<FirewallRuleInfo>(info => info.RemoteAddress == newIp)), Times.Once);
        _ruleManager.Verify(r => r.AddRule(It.IsAny<FirewallRuleInfo>()), Times.Never);
        _ruleManager.Verify(r => r.RemoveRule(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void RefreshLocalAddressRules_NoExistingRuleNewInterfaceUp_AddsRule()
    {
        // Arrange — no existing local address rules (machine had no interfaces up), new IP appears
        const string newIp = "10.8.0.1";
        _ruleManager.Setup(r => r.GetRulesByGroup("RunFence")).Returns([]);
        _networkInfo.Setup(n => n.GetLocalAddresses()).Returns([newIp]);
        var addedRules = new List<FirewallRuleInfo>();
        _ruleManager.Setup(r => r.AddRule(It.IsAny<FirewallRuleInfo>()))
            .Callback<FirewallRuleInfo>(addedRules.Add);
        var service = BuildService();

        // Act
        var changed = service.RefreshLocalAddressRules(Sid, Username,
            new FirewallAccountSettings { AllowLocalhost = false });

        // Assert — rule created (EnsureRule adds when missing), changed = true
        Assert.True(changed);
        Assert.Single(addedRules, r => r.Name.Contains("Local Addresses") && r.Name.Contains("IPv4")
                                                                          && r.RemoteAddress == newIp);
    }

    [Fact]
    public void RefreshLocalAddressRules_AllowLocalhostTrue_ReturnsFalseWithoutQueryingRules()
    {
        // Arrange — AllowLocalhost=true means no local address block rules to refresh
        var service = BuildService();

        // Act
        var changed = service.RefreshLocalAddressRules(Sid, Username,
            new FirewallAccountSettings { AllowLocalhost = true });

        // Assert
        Assert.False(changed);
        _ruleManager.Verify(r => r.GetRulesByGroup(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void ApplyFirewallRules_BlockLocalhostWithLocalIps_AddsLocalAddressBlockRules()
    {
        // Arrange — local address block rules must be added for own non-loopback IPs when
        // localhost is blocked, covering self-connection paths not handled by WFP loopback filters.
        const string localLanIp = "192.168.1.5";
        const string localPublicIp = "203.0.113.5";
        const string localIpv6 = "fe80::1"; // link-local IPv6 on the same adapter
        SetupNoExistingRulesAndNoDnsServers();
        _networkInfo.Setup(n => n.GetLocalAddresses()).Returns([localLanIp, localPublicIp, localIpv6]);
        var addedRules = new List<FirewallRuleInfo>();
        _ruleManager.Setup(r => r.AddRule(It.IsAny<FirewallRuleInfo>()))
            .Callback<FirewallRuleInfo>(addedRules.Add);
        var service = BuildService();

        // Act — only localhost blocked; internet and LAN allowed
        service.ApplyFirewallRules(Sid, Username, new FirewallAccountSettings
            { AllowInternet = true, AllowLocalhost = false, AllowLan = true });

        // Assert — exactly 2 local address block rules (IPv4 + IPv6), no internet/LAN rules
        Assert.Equal(2, addedRules.Count);
        var ipv4Rule = Assert.Single(addedRules, r => r.Name.Contains("Local Addresses") && r.Name.Contains("IPv4"));
        var ipv6Rule = Assert.Single(addedRules, r => r.Name.Contains("Local Addresses") && r.Name.Contains("IPv6"));
        Assert.Contains(localLanIp, ipv4Rule.RemoteAddress);
        Assert.Contains(localPublicIp, ipv4Rule.RemoteAddress);
        Assert.Contains(localIpv6, ipv6Rule.RemoteAddress);
    }

    [Fact]
    public void ApplyFirewallRules_AllowLocalhostWithExistingLocalAddressRules_RemovesLocalAddressRules()
    {
        // Arrange — existing local address block rules from a prior localhost-blocked state
        var existingIpv4 = MakeRule($"RunFence Block Local Addresses IPv4 ({Username})");
        var existingIpv6 = MakeRule($"RunFence Block Local Addresses IPv6 ({Username})");
        _ruleManager.Setup(r => r.GetRulesByGroup("RunFence")).Returns([existingIpv4, existingIpv6]);
        _networkInfo.Setup(n => n.GetDnsServerAddresses()).Returns([]);
        _networkInfo.Setup(n => n.GetLocalAddresses()).Returns([]);
        var service = BuildService();

        // Act — localhost now allowed
        service.ApplyFirewallRules(Sid, Username, new FirewallAccountSettings
            { AllowInternet = true, AllowLocalhost = true, AllowLan = true });

        // Assert — both local address rules removed
        _ruleManager.Verify(r => r.RemoveRule(existingIpv4.Name), Times.Once);
        _ruleManager.Verify(r => r.RemoveRule(existingIpv6.Name), Times.Once);
        _ruleManager.Verify(r => r.AddRule(It.IsAny<FirewallRuleInfo>()), Times.Never);
    }

    [Fact]
    public void ApplyFirewallRules_BlockLocalhostNoLocalAddresses_NoLocalAddressRulesAdded()
    {
        // Arrange — no network interfaces up → no local address rules should be created
        SetupNoExistingRulesAndNoDnsServers();
        var addedRules = new List<FirewallRuleInfo>();
        _ruleManager.Setup(r => r.AddRule(It.IsAny<FirewallRuleInfo>()))
            .Callback<FirewallRuleInfo>(addedRules.Add);
        var service = BuildService();

        // Act
        service.ApplyFirewallRules(Sid, Username, new FirewallAccountSettings
            { AllowInternet = true, AllowLocalhost = false, AllowLan = true });

        // Assert — no rules at all (empty remote address → EnsureRule skips creation)
        Assert.Empty(addedRules);
        _wfpBlocker.Verify(w => w.Apply(Sid, true), Times.Once);
    }

    [Fact]
    public void ApplyFirewallRules_BlockLanLocalhostAllowed_ExcludesLocalLanIpFromLanRule()
    {
        // Arrange — a local IP in the LAN range should be carved out of the LAN block rule
        // when AllowLocalhost=true, so self-connections via the LAN interface still work.
        const string localLanIp = "192.168.1.5";
        _ruleManager.Setup(r => r.GetRulesByGroup("RunFence")).Returns([]);
        _networkInfo.Setup(n => n.GetDnsServerAddresses()).Returns([]);

        var settings = new FirewallAccountSettings { AllowInternet = true, AllowLocalhost = true, AllowLan = false };

        var rulesWithLocal = new List<FirewallRuleInfo>();
        var rulesWithoutLocal = new List<FirewallRuleInfo>();

        // With local address present
        _networkInfo.Setup(n => n.GetLocalAddresses()).Returns([localLanIp]);
        _ruleManager.Setup(r => r.AddRule(It.IsAny<FirewallRuleInfo>()))
            .Callback<FirewallRuleInfo>(rulesWithLocal.Add);
        BuildService().ApplyFirewallRules(Sid, Username, settings);

        // Without local address
        _networkInfo.Setup(n => n.GetLocalAddresses()).Returns([]);
        _ruleManager.Setup(r => r.AddRule(It.IsAny<FirewallRuleInfo>()))
            .Callback<FirewallRuleInfo>(rulesWithoutLocal.Add);
        BuildService().ApplyFirewallRules(Sid, Username, settings);

        // Assert — local IP carved out narrows the LAN block range
        var lanWithLocal = rulesWithLocal.First(r => r.Name.Contains("LAN") && r.Name.Contains("IPv4"));
        var lanWithoutLocal = rulesWithoutLocal.First(r => r.Name.Contains("LAN") && r.Name.Contains("IPv4"));
        Assert.NotEqual(lanWithLocal.RemoteAddress, lanWithoutLocal.RemoteAddress);
        Assert.DoesNotContain(localLanIp + "/32", lanWithLocal.RemoteAddress);
    }

    [Fact]
    public void ApplyFirewallRules_BlockInternetLocalhostAllowed_ExcludesLocalPublicIpFromInternetRule()
    {
        // Arrange — a local public IP (e.g. from a VPN adapter) should be carved out of the
        // internet block rule when AllowLocalhost=true.
        const string localPublicIp = "203.0.113.5"; // TEST-NET-3, not in RFC 1918 or base exclusions
        _ruleManager.Setup(r => r.GetRulesByGroup("RunFence")).Returns([]);
        _networkInfo.Setup(n => n.GetDnsServerAddresses()).Returns([]);

        var settings = new FirewallAccountSettings { AllowInternet = false, AllowLocalhost = true, AllowLan = true };

        var rulesWithLocal = new List<FirewallRuleInfo>();
        var rulesWithoutLocal = new List<FirewallRuleInfo>();

        // With local public IP
        _networkInfo.Setup(n => n.GetLocalAddresses()).Returns([localPublicIp]);
        _ruleManager.Setup(r => r.AddRule(It.IsAny<FirewallRuleInfo>()))
            .Callback<FirewallRuleInfo>(rulesWithLocal.Add);
        BuildService().ApplyFirewallRules(Sid, Username, settings);

        // Without local IP
        _networkInfo.Setup(n => n.GetLocalAddresses()).Returns([]);
        _ruleManager.Setup(r => r.AddRule(It.IsAny<FirewallRuleInfo>()))
            .Callback<FirewallRuleInfo>(rulesWithoutLocal.Add);
        BuildService().ApplyFirewallRules(Sid, Username, settings);

        // Assert — public IP carved out narrows the internet block range
        var intWithLocal = rulesWithLocal.First(r => r.Name.Contains("Internet") && r.Name.Contains("IPv4"));
        var intWithoutLocal = rulesWithoutLocal.First(r => r.Name.Contains("Internet") && r.Name.Contains("IPv4"));
        Assert.NotEqual(intWithLocal.RemoteAddress, intWithoutLocal.RemoteAddress);
        Assert.DoesNotContain(localPublicIp + "/32", intWithLocal.RemoteAddress);
    }

    [Fact]
    public void ApplyFirewallRules_BlockInternetLocalhostBlocked_DoesNotExcludeLocalAddresses()
    {
        // Arrange — when AllowLocalhost=false, local addresses must NOT be carved out
        // (the intent is to block all outbound traffic, including self-connections).
        const string localPublicIp = "203.0.113.5";
        _ruleManager.Setup(r => r.GetRulesByGroup("RunFence")).Returns([]);
        _networkInfo.Setup(n => n.GetDnsServerAddresses()).Returns([]);

        var settingsLocalhostBlocked = new FirewallAccountSettings
            { AllowInternet = false, AllowLocalhost = false, AllowLan = true };
        var settingsNoLocalIp = new FirewallAccountSettings
            { AllowInternet = false, AllowLocalhost = false, AllowLan = true };

        var rulesWithLocal = new List<FirewallRuleInfo>();
        var rulesWithoutLocal = new List<FirewallRuleInfo>();

        // With local IP but AllowLocalhost=false
        _networkInfo.Setup(n => n.GetLocalAddresses()).Returns([localPublicIp]);
        _ruleManager.Setup(r => r.AddRule(It.IsAny<FirewallRuleInfo>()))
            .Callback<FirewallRuleInfo>(rulesWithLocal.Add);
        BuildService().ApplyFirewallRules(Sid, Username, settingsLocalhostBlocked);

        // Without local IP and AllowLocalhost=false
        _networkInfo.Setup(n => n.GetLocalAddresses()).Returns([]);
        _ruleManager.Setup(r => r.AddRule(It.IsAny<FirewallRuleInfo>()))
            .Callback<FirewallRuleInfo>(rulesWithoutLocal.Add);
        BuildService().ApplyFirewallRules(Sid, Username, settingsNoLocalIp);

        // Assert — ranges are identical: local addresses are not excluded when localhost is blocked
        var intWithLocal = rulesWithLocal.First(r => r.Name.Contains("Internet") && r.Name.Contains("IPv4"));
        var intWithoutLocal = rulesWithoutLocal.First(r => r.Name.Contains("Internet") && r.Name.Contains("IPv4"));
        Assert.Equal(intWithLocal.RemoteAddress, intWithoutLocal.RemoteAddress);
    }

    [Fact]
    public void EnforceAll_OrphanedSid_WfpLocalhostBlockCleared()
    {
        // Arrange — rule list contains a rule whose SID is not in any active firewall settings
        // (i.e. the rule is orphaned). BUG-2: CleanupOrphanedRulesFromList must call
        // _wfpBlocker.Apply(orphanSid, block: false) after removing the INetFw rule so
        // any lingering WFP filter for that SID is also cleared.
        const string orphanSid = "S-1-5-21-0-0-0-7777";
        var orphanRule = MakeRule("RunFence Block Internet IPv4 (orphan)", orphanSid);
        _ruleManager.Setup(r => r.GetRulesByGroup("RunFence")).Returns([orphanRule]);

        // Database has no entry for OrphanSid — so the rule is orphaned
        var database = new AppDatabase();
        var service = BuildService();

        // Act
        service.EnforceAll(database);

        // Assert — WFP blocker cleared for the orphaned SID
        _wfpBlocker.Verify(w => w.Apply(orphanSid, false), Times.Once);
        // INetFw rule also removed
        _ruleManager.Verify(r => r.RemoveRule(orphanRule.Name), Times.Once);
    }

    [Fact]
    public void EnforceAll_OrphanedSidWfpFails_DoesNotThrow()
    {
        // Arrange — WFP cleanup throws but EnforceAll must still not propagate the exception
        const string orphanSid = "S-1-5-21-0-0-0-8888";
        var orphanRule = MakeRule("RunFence Block Internet IPv4 (orphan2)", orphanSid);
        _ruleManager.Setup(r => r.GetRulesByGroup("RunFence")).Returns([orphanRule]);
        _wfpBlocker.Setup(w => w.Apply(orphanSid, false))
            .Throws(new Win32Exception(5)); // Access denied

        var database = new AppDatabase();
        var service = BuildService();

        // Act & Assert — must not throw even when WFP cleanup fails
        var ex = Record.Exception(() => service.EnforceAll(database));
        Assert.Null(ex);
    }

    [Fact]
    public void ApplyFirewallRules_AllowlistEmpty_DnsServersNotExcludedFromInternetRange()
    {
        // Arrange — empty allowlist → DNS server exclusion is NOT applied.
        // With no allowlist entries, the internet block range is the full internet CIDR set,
        // which is the same whether or not DNS servers are present.
        const string dnsServer = "8.8.8.8";

        var addedRulesNoDns = new List<FirewallRuleInfo>();
        var addedRulesWithDns = new List<FirewallRuleInfo>();

        // First: no DNS servers reported
        _ruleManager.Setup(r => r.GetRulesByGroup("RunFence")).Returns([]);
        _networkInfo.Setup(n => n.GetDnsServerAddresses()).Returns([]);
        _networkInfo.Setup(n => n.GetLocalAddresses()).Returns([]);
        _ruleManager.Setup(r => r.AddRule(It.IsAny<FirewallRuleInfo>()))
            .Callback<FirewallRuleInfo>(r => addedRulesNoDns.Add(r));
        BuildService().ApplyFirewallRules(Sid, Username, new FirewallAccountSettings
        {
            AllowInternet = false, AllowLocalhost = true, AllowLan = true, Allowlist = []
        });

        // Second: DNS server reported but allowlist still empty
        _networkInfo.Setup(n => n.GetDnsServerAddresses()).Returns([dnsServer]);
        _ruleManager.Setup(r => r.AddRule(It.IsAny<FirewallRuleInfo>()))
            .Callback<FirewallRuleInfo>(r => addedRulesWithDns.Add(r));
        BuildService().ApplyFirewallRules(Sid, Username, new FirewallAccountSettings
        {
            AllowInternet = false, AllowLocalhost = true, AllowLan = true, Allowlist = []
        });

        // Assert — ranges are identical: DNS server is NOT excluded when allowlist is empty
        var ipv4NoDns = addedRulesNoDns.First(r => r.Name.Contains("Internet") && r.Name.Contains("IPv4"));
        var ipv4WithDns = addedRulesWithDns.First(r => r.Name.Contains("Internet") && r.Name.Contains("IPv4"));
        Assert.Equal(ipv4NoDns.RemoteAddress, ipv4WithDns.RemoteAddress);
    }
}