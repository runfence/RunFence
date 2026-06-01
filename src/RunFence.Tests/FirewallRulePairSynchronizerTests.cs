using RunFence.Core.Models;
using RunFence.Firewall;
using Xunit;

namespace RunFence.Tests;

public class FirewallRulePairSynchronizerTests
{
    private const string Sid = "S-1-5-21-1234567890-1234567890-1234567890-1001";
    private const string Username = "testuser";

    [Fact]
    public void ApplyInternetRules_BlockingDisabled_RemovesExistingRules()
    {
        var context = CreateContext();
        var existing = new[]
        {
            CreateRule(FirewallRuleNames.InternetIPv4RuleName(Username), Sid, "0.0.0.0/0"),
            CreateRule(FirewallRuleNames.InternetIPv6RuleName(Username), Sid, "::/0")
        };
        var settings = new FirewallAccountSettings { AllowInternet = true };

        var pendingDomains = context.Synchronizer.ApplyInternetRules(
            Sid,
            Username,
            settings,
            existing,
            new Dictionary<string, IReadOnlyList<string>>());

        Assert.Empty(pendingDomains);
        Assert.Equal(
            [
                FirewallRuleNames.InternetIPv4RuleName(Username),
                FirewallRuleNames.InternetIPv6RuleName(Username)
            ],
            context.RuleManager.RemovedRuleNames);
    }

    [Fact]
    public void ApplyInternetRules_BlockingEnabled_AddsExactRulesAndReturnsPendingDomains()
    {
        var context = CreateContext(dnsServerAddresses: ["8.8.8.8"], localAddresses: ["127.0.0.1"]);
        var settings = new FirewallAccountSettings
        {
            AllowInternet = false,
            Allowlist =
            [
                new FirewallAllowlistEntry { Value = "example.com", IsDomain = true },
                new FirewallAllowlistEntry { Value = "1.2.3.4", IsDomain = false }
            ]
        };

        var pendingDomains = context.Synchronizer.ApplyInternetRules(
            Sid,
            Username,
            settings,
            [],
            new Dictionary<string, IReadOnlyList<string>>());
        var expectedAddresses = context.AddressBuilder.ComputeInternetAddresses(
            Sid,
            settings,
            new Dictionary<string, IReadOnlyList<string>>());

        Assert.Equal([new FirewallPendingDomainResolution(Sid, "example.com")], pendingDomains);
        AssertRule(
            context.RuleManager.StoredRules[FirewallRuleNames.InternetIPv4RuleName(Username)],
            FirewallRuleNames.InternetIPv4RuleName(Username),
            Sid,
            expectedAddresses.IPv4Address);
        AssertRule(
            context.RuleManager.StoredRules[FirewallRuleNames.InternetIPv6RuleName(Username)],
            FirewallRuleNames.InternetIPv6RuleName(Username),
            Sid,
            expectedAddresses.IPv6Address);
    }

    [Fact]
    public void ApplyLanRules_BlockingEnabled_AddsLanRules()
    {
        var context = CreateContext();
        var settings = new FirewallAccountSettings { AllowLan = false };

        var pendingDomains = context.Synchronizer.ApplyLanRules(
            Sid,
            Username,
            settings,
            [],
            new Dictionary<string, IReadOnlyList<string>>());

        Assert.Empty(pendingDomains);
        AssertRule(
            context.RuleManager.StoredRules[FirewallRuleNames.LanIPv4RuleName(Username)],
            FirewallRuleNames.LanIPv4RuleName(Username),
            Sid,
            "10.0.0.0/8,172.16.0.0/12,192.168.0.0/16,169.254.0.0/16,100.64.0.0/10");
        AssertRule(
            context.RuleManager.StoredRules[FirewallRuleNames.LanIPv6RuleName(Username)],
            FirewallRuleNames.LanIPv6RuleName(Username),
            Sid,
            "fe80::/10,fc00::/7");
    }

    [Fact]
    public void RemoveLocalhostLegacyRules_RemovesLocalhostRulesWhenPresent()
    {
        var context = CreateContext();
        context.RuleManager.StoredRules.Clear();
        context.RuleManager.StoredRules[FirewallRuleNames.LocalhostIPv4RuleName(Username)] = CreateRule(
            FirewallRuleNames.LocalhostIPv4RuleName(Username),
            Sid,
            "127.0.0.1");
        context.RuleManager.StoredRules[FirewallRuleNames.LocalhostIPv6RuleName(Username)] = CreateRule(
            FirewallRuleNames.LocalhostIPv6RuleName(Username),
            Sid,
            "::1");
        var existing = context.RuleManager.StoredRules.Values.ToList();

        context.Synchronizer.RemoveLocalhostLegacyRules(Username, existing);

        Assert.Contains(FirewallRuleNames.LocalhostIPv4RuleName(Username), context.RuleManager.RemovedRuleNames);
        Assert.Contains(FirewallRuleNames.LocalhostIPv6RuleName(Username), context.RuleManager.RemovedRuleNames);
        Assert.Empty(context.RuleManager.StoredRules);
    }

    [Fact]
    public void RemoveLocalhostLegacyRules_NoExistingRules_DoesNothing()
    {
        var context = CreateContext();

        context.Synchronizer.RemoveLocalhostLegacyRules(Username, []);

        Assert.Empty(context.RuleManager.RemovedRuleNames);
        Assert.Empty(context.RuleManager.StoredRules);
    }

    [Fact]
    public void RefreshAllowlistRules_NoDesiredBlocking_IsNoOp()
    {
        var context = CreateContext();
        var changed = context.Synchronizer.RefreshAllowlistRules(
            Sid,
            Username,
            new FirewallAccountSettings { AllowInternet = true, AllowLan = true },
            new Dictionary<string, IReadOnlyList<string>>(),
            []);

        Assert.False(changed);
        Assert.Empty(context.RuleManager.StoredRules);
        Assert.Empty(context.RuleManager.UpdatedRules);
        Assert.Empty(context.RuleManager.RemovedRuleNames);
    }

    [Fact]
    public void RefreshAllowlistRules_StaleRule_UpdatesOnlyChangedRule()
    {
        var context = CreateContext();
        var settings = new FirewallAccountSettings { AllowInternet = false, AllowLan = true };
        var expectedAddresses = context.AddressBuilder.ComputeInternetAddresses(
            Sid,
            settings,
            new Dictionary<string, IReadOnlyList<string>>());
        var existing = new[]
        {
            CreateRule(FirewallRuleNames.InternetIPv4RuleName(Username), Sid, "1.2.3.4"),
            CreateRule(FirewallRuleNames.InternetIPv6RuleName(Username), Sid, expectedAddresses.IPv6Address)
        };

        var changed = context.Synchronizer.RefreshAllowlistRules(
            Sid,
            Username,
            settings,
            new Dictionary<string, IReadOnlyList<string>>(),
            existing);

        Assert.True(changed);
        Assert.Contains(FirewallRuleNames.InternetIPv4RuleName(Username), context.RuleManager.UpdatedRules.Keys);
        Assert.DoesNotContain(FirewallRuleNames.InternetIPv6RuleName(Username), context.RuleManager.UpdatedRules.Keys);
        AssertRule(
            context.RuleManager.UpdatedRules[FirewallRuleNames.InternetIPv4RuleName(Username)],
            FirewallRuleNames.InternetIPv4RuleName(Username),
            Sid,
            expectedAddresses.IPv4Address);
    }

    [Fact]
    public void RefreshAllowlistRules_SkipsWrongPrefixRulesAndWritesExactIPv4IPv6Rules()
    {
        var context = CreateContext();
        var settings = new FirewallAccountSettings { AllowInternet = false, AllowLan = true };
        var expectedAddresses = context.AddressBuilder.ComputeInternetAddresses(
            Sid,
            settings,
            new Dictionary<string, IReadOnlyList<string>>());
        var staleUser = $"{Username}-legacy";
        var staleV4Name = FirewallRuleNames.InternetIPv4RuleName(staleUser);
        var staleV6Name = FirewallRuleNames.InternetIPv6RuleName(staleUser);

        context.RuleManager.StoredRules.Add(staleV4Name, CreateRule(staleV4Name, Sid, "10.10.10.10"));
        context.RuleManager.StoredRules.Add(staleV6Name, CreateRule(staleV6Name, Sid, "2001:db8::10"));
        var existing = context.RuleManager.StoredRules.Values.ToList();

        var changed = context.Synchronizer.RefreshAllowlistRules(
            Sid,
            Username,
            settings,
            new Dictionary<string, IReadOnlyList<string>>(),
            existing);

        Assert.True(changed);
        Assert.Equal(2, context.RuleManager.StoredRules.Count);
        Assert.Contains(FirewallRuleNames.InternetIPv4RuleName(Username), context.RuleManager.StoredRules.Keys);
        Assert.Contains(FirewallRuleNames.InternetIPv6RuleName(Username), context.RuleManager.StoredRules.Keys);
        Assert.DoesNotContain(staleV4Name, context.RuleManager.StoredRules.Keys);
        Assert.DoesNotContain(staleV6Name, context.RuleManager.StoredRules.Keys);
        AssertRule(
            context.RuleManager.StoredRules[FirewallRuleNames.InternetIPv4RuleName(Username)],
            FirewallRuleNames.InternetIPv4RuleName(Username),
            Sid,
            expectedAddresses.IPv4Address);
        AssertRule(
            context.RuleManager.StoredRules[FirewallRuleNames.InternetIPv6RuleName(Username)],
            FirewallRuleNames.InternetIPv6RuleName(Username),
            Sid,
            expectedAddresses.IPv6Address);
    }

    [Fact]
    public void RefreshAllowlistRules_DesiredRulesAlreadyMatch_RemovesOnlyStaleVariants()
    {
        var context = CreateContext();
        var settings = new FirewallAccountSettings { AllowInternet = false, AllowLan = true };
        var expectedAddresses = context.AddressBuilder.ComputeInternetAddresses(
            Sid,
            settings,
            new Dictionary<string, IReadOnlyList<string>>());
        var staleV4Name = $"{FirewallRuleNames.InternetIPv4RuleName(Username)}-stale";
        var staleV6Name = $"{FirewallRuleNames.InternetIPv6RuleName(Username)}-stale";

        context.RuleManager.StoredRules[FirewallRuleNames.InternetIPv4RuleName(Username)] =
            CreateRule(FirewallRuleNames.InternetIPv4RuleName(Username), Sid, expectedAddresses.IPv4Address);
        context.RuleManager.StoredRules[FirewallRuleNames.InternetIPv6RuleName(Username)] =
            CreateRule(FirewallRuleNames.InternetIPv6RuleName(Username), Sid, expectedAddresses.IPv6Address);
        context.RuleManager.StoredRules[staleV4Name] = CreateRule(staleV4Name, Sid, "10.10.10.10");
        context.RuleManager.StoredRules[staleV6Name] = CreateRule(staleV6Name, Sid, "2001:db8::10");
        var existing = context.RuleManager.StoredRules.Values.ToList();

        var changed = context.Synchronizer.RefreshAllowlistRules(
            Sid,
            Username,
            settings,
            new Dictionary<string, IReadOnlyList<string>>(),
            existing);

        Assert.True(changed);
        Assert.Equal(2, context.RuleManager.RemovedRuleNames.Count);
        Assert.Contains(staleV4Name, context.RuleManager.RemovedRuleNames);
        Assert.Contains(staleV6Name, context.RuleManager.RemovedRuleNames);
        Assert.Empty(context.RuleManager.UpdatedRules);
        Assert.Equal(
            2,
            context.RuleManager.StoredRules.Count);
        AssertRule(
            context.RuleManager.StoredRules[FirewallRuleNames.InternetIPv4RuleName(Username)],
            FirewallRuleNames.InternetIPv4RuleName(Username),
            Sid,
            expectedAddresses.IPv4Address);
        AssertRule(
            context.RuleManager.StoredRules[FirewallRuleNames.InternetIPv6RuleName(Username)],
            FirewallRuleNames.InternetIPv6RuleName(Username),
            Sid,
            expectedAddresses.IPv6Address);
    }

    [Fact]
    public void ApplyLocalAddressRules_BlockingEnabled_AddsLocalAddressRules()
    {
        var context = CreateContext(localAddresses: ["127.0.0.1", "192.168.0.55", "::1", "fe80::1"]);
        var settings = new FirewallAccountSettings { AllowLocalhost = false };

        var pendingDomains = context.Synchronizer.ApplyLocalAddressRules(
            Sid,
            Username,
            settings,
            []);

        Assert.Empty(pendingDomains);
        AssertRule(
            context.RuleManager.StoredRules[FirewallRuleNames.LocalAddressIPv4RuleName(Username)],
            FirewallRuleNames.LocalAddressIPv4RuleName(Username),
            Sid,
            "127.0.0.1,192.168.0.55");
        AssertRule(
            context.RuleManager.StoredRules[FirewallRuleNames.LocalAddressIPv6RuleName(Username)],
            FirewallRuleNames.LocalAddressIPv6RuleName(Username),
            Sid,
            "::1,fe80::1");
    }

    [Fact]
    public void ApplyLocalAddressRules_LocalhostAllowed_RemovesExistingRulesAndReturnsNoPendingResolutions()
    {
        var context = CreateContext();
        var existing = new[]
        {
            CreateRule(FirewallRuleNames.LocalAddressIPv4RuleName(Username), Sid, "127.0.0.1"),
            CreateRule(FirewallRuleNames.LocalAddressIPv6RuleName(Username), Sid, "::1")
        };
        context.RuleManager.StoredRules[FirewallRuleNames.LocalAddressIPv4RuleName(Username)] = existing[0];
        context.RuleManager.StoredRules[FirewallRuleNames.LocalAddressIPv6RuleName(Username)] = existing[1];

        var settings = new FirewallAccountSettings { AllowLocalhost = true };

        var pendingDomains = context.Synchronizer.ApplyLocalAddressRules(
            Sid,
            Username,
            settings,
            existing);

        Assert.Empty(pendingDomains);
        Assert.Equal(
            [
                FirewallRuleNames.LocalAddressIPv4RuleName(Username),
                FirewallRuleNames.LocalAddressIPv6RuleName(Username)
            ],
            context.RuleManager.RemovedRuleNames);
        Assert.Empty(context.RuleManager.StoredRules);
    }

    [Fact]
    public void RefreshLocalAddressRules_EmptyDesiredAddresses_RemovesExistingRules()
    {
        var context = CreateContext(localAddresses: []);
        var existing = new[]
        {
            CreateRule(FirewallRuleNames.LocalAddressIPv4RuleName(Username), Sid, "127.0.0.1"),
            CreateRule(FirewallRuleNames.LocalAddressIPv6RuleName(Username), Sid, "::1")
        };
        var settings = new FirewallAccountSettings { AllowLocalhost = false };

        var changed = context.Synchronizer.RefreshLocalAddressRules(
            Sid,
            Username,
            settings,
            existing);

        Assert.True(changed);
        Assert.Equal(
            [
                FirewallRuleNames.LocalAddressIPv4RuleName(Username),
                FirewallRuleNames.LocalAddressIPv6RuleName(Username)
            ],
            context.RuleManager.RemovedRuleNames);
    }

    [Fact]
    public void RefreshLocalAddressRules_DesiredStateAlreadyMatches_IsNoOp()
    {
        var context = CreateContext(localAddresses: ["127.0.0.1", "::1"]);
        var existing = new[]
        {
            CreateRule(FirewallRuleNames.LocalAddressIPv4RuleName(Username), Sid, "127.0.0.1"),
            CreateRule(FirewallRuleNames.LocalAddressIPv6RuleName(Username), Sid, "::1")
        };
        var settings = new FirewallAccountSettings { AllowLocalhost = false };

        var changed = context.Synchronizer.RefreshLocalAddressRules(
            Sid,
            Username,
            settings,
            existing);

        Assert.False(changed);
        Assert.Empty(context.RuleManager.UpdatedRules);
        Assert.Empty(context.RuleManager.RemovedRuleNames);
        Assert.Empty(context.RuleManager.StoredRules);
    }

    [Fact]
    public void RefreshLocalAddressRules_StaleRule_UpdatesChangedRuleWithExactState()
    {
        var context = CreateContext(localAddresses: ["127.0.0.1", "::1"]);
        var existing = new[]
        {
            CreateRule(FirewallRuleNames.LocalAddressIPv4RuleName(Username), Sid, "10.0.0.1"),
            CreateRule(FirewallRuleNames.LocalAddressIPv6RuleName(Username), Sid, "::1")
        };
        var settings = new FirewallAccountSettings { AllowLocalhost = false };

        var changed = context.Synchronizer.RefreshLocalAddressRules(
            Sid,
            Username,
            settings,
            existing);

        Assert.True(changed);
        Assert.Contains(FirewallRuleNames.LocalAddressIPv4RuleName(Username), context.RuleManager.UpdatedRules.Keys);
        Assert.DoesNotContain(FirewallRuleNames.LocalAddressIPv6RuleName(Username), context.RuleManager.UpdatedRules.Keys);
        AssertRule(
            context.RuleManager.UpdatedRules[FirewallRuleNames.LocalAddressIPv4RuleName(Username)],
            FirewallRuleNames.LocalAddressIPv4RuleName(Username),
            Sid,
            "127.0.0.1");
    }

    [Fact]
    public void RefreshLocalAddressRules_SkipsWrongPrefixRulesAndWritesExactIPv4IPv6Rules()
    {
        var context = CreateContext(localAddresses: ["127.0.0.1", "::1"]);
        var staleUser = $"{Username}-legacy";
        var staleV4Name = FirewallRuleNames.LocalAddressIPv4RuleName(staleUser);
        var staleV6Name = FirewallRuleNames.LocalAddressIPv6RuleName(staleUser);

        context.RuleManager.StoredRules.Add(staleV4Name, CreateRule(staleV4Name, Sid, "10.10.10.10"));
        context.RuleManager.StoredRules.Add(staleV6Name, CreateRule(staleV6Name, Sid, "2001:db8::10"));

        var existing = context.RuleManager.StoredRules.Values.ToList();
        var settings = new FirewallAccountSettings { AllowLocalhost = false };

        var changed = context.Synchronizer.RefreshLocalAddressRules(
            Sid,
            Username,
            settings,
            existing);

        Assert.True(changed);
        Assert.Equal(2, context.RuleManager.StoredRules.Count);
        Assert.Contains(FirewallRuleNames.LocalAddressIPv4RuleName(Username), context.RuleManager.StoredRules.Keys);
        Assert.Contains(FirewallRuleNames.LocalAddressIPv6RuleName(Username), context.RuleManager.StoredRules.Keys);
        Assert.DoesNotContain(staleV4Name, context.RuleManager.StoredRules.Keys);
        Assert.DoesNotContain(staleV6Name, context.RuleManager.StoredRules.Keys);
        AssertRule(
            context.RuleManager.StoredRules[FirewallRuleNames.LocalAddressIPv4RuleName(Username)],
            FirewallRuleNames.LocalAddressIPv4RuleName(Username),
            Sid,
            "127.0.0.1");
        AssertRule(
            context.RuleManager.StoredRules[FirewallRuleNames.LocalAddressIPv6RuleName(Username)],
            FirewallRuleNames.LocalAddressIPv6RuleName(Username),
            Sid,
            "::1");
    }

    [Fact]
    public void RefreshLocalAddressRules_DesiredRulesAlreadyMatch_RemovesOnlyStaleVariants()
    {
        var context = CreateContext(localAddresses: ["127.0.0.1", "::1"]);
        var staleV4Name = $"{FirewallRuleNames.LocalAddressIPv4RuleName(Username)}-stale";
        var staleV6Name = $"{FirewallRuleNames.LocalAddressIPv6RuleName(Username)}-stale";

        context.RuleManager.StoredRules[FirewallRuleNames.LocalAddressIPv4RuleName(Username)] =
            CreateRule(FirewallRuleNames.LocalAddressIPv4RuleName(Username), Sid, "127.0.0.1");
        context.RuleManager.StoredRules[FirewallRuleNames.LocalAddressIPv6RuleName(Username)] =
            CreateRule(FirewallRuleNames.LocalAddressIPv6RuleName(Username), Sid, "::1");
        context.RuleManager.StoredRules[staleV4Name] = CreateRule(staleV4Name, Sid, "10.10.10.10");
        context.RuleManager.StoredRules[staleV6Name] = CreateRule(staleV6Name, Sid, "2001:db8::10");
        var existing = context.RuleManager.StoredRules.Values.ToList();
        var settings = new FirewallAccountSettings { AllowLocalhost = false };

        var changed = context.Synchronizer.RefreshLocalAddressRules(
            Sid,
            Username,
            settings,
            existing);

        Assert.True(changed);
        Assert.Equal(2, context.RuleManager.RemovedRuleNames.Count);
        Assert.Contains(staleV4Name, context.RuleManager.RemovedRuleNames);
        Assert.Contains(staleV6Name, context.RuleManager.RemovedRuleNames);
        Assert.Empty(context.RuleManager.UpdatedRules);
        AssertRule(
            context.RuleManager.StoredRules[FirewallRuleNames.LocalAddressIPv4RuleName(Username)],
            FirewallRuleNames.LocalAddressIPv4RuleName(Username),
            Sid,
            "127.0.0.1");
        AssertRule(
            context.RuleManager.StoredRules[FirewallRuleNames.LocalAddressIPv6RuleName(Username)],
            FirewallRuleNames.LocalAddressIPv6RuleName(Username),
            Sid,
            "::1");
    }

    private static TestContext CreateContext(
        IReadOnlyList<string>? dnsServerAddresses = null,
        IReadOnlyList<string>? localAddresses = null)
    {
        var ruleManager = new FakeFirewallRuleManager();
        var addressBuilder = new FirewallAddressExclusionBuilder(
            new FirewallAddressRangeBuilder(),
            new FakeNetworkInterfaceInfoProvider(
                dnsServerAddresses ?? [],
                localAddresses ?? []));
        return new TestContext(ruleManager, addressBuilder, new FirewallRulePairSynchronizer(ruleManager, addressBuilder));
    }

    private static FirewallRuleInfo CreateRule(string name, string sid, string remoteAddress)
        => new(
            name,
            FirewallSddlHelper.BuildSddl(sid),
            remoteAddress,
            Direction: 2,
            Action: 0,
            Protocol: 256,
            Grouping: FirewallConstants.RuleGrouping,
            Description: "Managed by RunFence");

    private static void AssertRule(FirewallRuleInfo actual, string expectedName, string sid, string expectedRemoteAddress)
    {
        Assert.Equal(expectedName, actual.Name);
        Assert.Equal(FirewallSddlHelper.BuildSddl(sid), actual.LocalUser);
        Assert.Equal(expectedRemoteAddress, actual.RemoteAddress);
        Assert.Equal(2, actual.Direction);
        Assert.Equal(0, actual.Action);
        Assert.Equal(256, actual.Protocol);
        Assert.Equal(FirewallConstants.RuleGrouping, actual.Grouping);
        Assert.Equal("Managed by RunFence", actual.Description);
    }

    private sealed record TestContext(
        FakeFirewallRuleManager RuleManager,
        FirewallAddressExclusionBuilder AddressBuilder,
        FirewallRulePairSynchronizer Synchronizer);

    private sealed class FakeNetworkInterfaceInfoProvider(
        IReadOnlyList<string> dnsServerAddresses,
        IReadOnlyList<string> localAddresses) : INetworkInterfaceInfoProvider
    {
        public IReadOnlyList<string> GetDnsServerAddresses() => dnsServerAddresses;

        public IReadOnlyList<string> GetLocalAddresses() => localAddresses;
    }

    private sealed class FakeFirewallRuleManager : IFirewallRuleManager
    {
        public Dictionary<string, FirewallRuleInfo> StoredRules { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, FirewallRuleInfo> UpdatedRules { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> RemovedRuleNames { get; } = [];

        public IReadOnlyList<FirewallRuleInfo> GetRulesByGroup(string grouping) => StoredRules.Values.ToList();

        public void AddRule(FirewallRuleInfo rule)
            => StoredRules[rule.Name] = rule;

        public void UpdateRule(string existingName, FirewallRuleInfo rule)
        {
            StoredRules.Remove(existingName);
            StoredRules[rule.Name] = rule;
            UpdatedRules[existingName] = rule;
        }

        public void RemoveRule(string name)
        {
            StoredRules.Remove(name);
            RemovedRuleNames.Add(name);
        }
    }
}
