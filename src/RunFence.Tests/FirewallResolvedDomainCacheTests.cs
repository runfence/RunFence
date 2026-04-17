using RunFence.Core.Models;
using RunFence.Firewall;
using Xunit;

namespace RunFence.Tests;

public class FirewallResolvedDomainCacheTests
{
    private const string Sid = "S-1-5-21-1000-1000-1000-1001";
    private const string OtherSid = "S-1-5-21-1000-1000-1000-1002";

    [Fact]
    public void ResolvedDomain_IsSharedAcrossAccountsWithSameAllowlistDomain()
    {
        var cache = new FirewallResolvedDomainCache();
        var firstSettings = SettingsWithDomains("example.com");
        var secondSettings = SettingsWithDomains("example.com");

        cache.UpdateResolvedDomainsAndGetChangedDomains(["example.com"], Resolved("example.com", "203.0.113.10"));

        Assert.Equal(["203.0.113.10"], cache.GetAccountSnapshot(firstSettings)["example.com"]);
        Assert.Equal(["203.0.113.10"], cache.GetAccountSnapshot(secondSettings)["example.com"]);
    }

    [Fact]
    public void ResolvedDomain_UsesCaseInsensitiveGlobalCacheEntry()
    {
        var cache = new FirewallResolvedDomainCache();

        cache.UpdateResolvedDomainsAndGetChangedDomains(["Example.COM"], Resolved("Example.COM", "203.0.113.10"));
        var snapshot = cache.GetAccountSnapshot(SettingsWithDomains("example.com"));

        Assert.Single(cache.GetGlobalSnapshot());
        Assert.Equal(["203.0.113.10"], snapshot["example.com"]);
    }

    [Fact]
    public void GetAccountSnapshot_FiltersGlobalCacheToSettingsAllowlist()
    {
        var cache = new FirewallResolvedDomainCache();
        cache.UpdateResolvedDomainsAndGetChangedDomains(
            ["first.example", "second.example"],
            new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["first.example"] = ["203.0.113.10"],
                ["second.example"] = ["203.0.113.11"]
            });

        var snapshot = cache.GetAccountSnapshot(SettingsWithDomains("second.example"));

        Assert.False(snapshot.ContainsKey("first.example"));
        Assert.Equal(["203.0.113.11"], snapshot["second.example"]);
    }

    [Fact]
    public void MarkRefreshSucceeded_ClearsDirtyStateOnlyForSpecifiedSid()
    {
        var cache = new FirewallResolvedDomainCache();
        var settings = SettingsWithDomains("example.com");
        cache.UpdateResolvedDomainsAndGetChangedDomains(["example.com"], Resolved("example.com", "203.0.113.10"));
        cache.MarkDirty(Sid, ["example.com"]);
        cache.MarkDirty(OtherSid, ["example.com"]);

        cache.MarkRefreshSucceeded(Sid, ["example.com"]);

        Assert.False(cache.GetRefreshDecision(Sid, settings, EmptyChangedDomains()).WasDirty);
        Assert.True(cache.GetRefreshDecision(OtherSid, settings, EmptyChangedDomains()).WasDirty);
    }

    [Fact]
    public void UpdateResolvedDomainsAndGetChangedDomains_ComparesAddressSetsIgnoringDuplicates()
    {
        var cache = new FirewallResolvedDomainCache();
        cache.UpdateResolvedDomainsAndGetChangedDomains(
            ["example.com"],
            new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["example.com"] = ["203.0.113.10", "203.0.113.11"]
            });

        var changed = cache.UpdateResolvedDomainsAndGetChangedDomains(
            ["example.com"],
            new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["example.com"] = ["203.0.113.10", "203.0.113.10"]
            });

        Assert.Equal(["example.com"], changed);
    }

    [Fact]
    public void MarkDirtyForChangedDomains_MarksEveryEligibleAccountThatAllowlistsChangedDomain()
    {
        var cache = new FirewallResolvedDomainCache();
        var changedDomains = cache.UpdateResolvedDomainsAndGetChangedDomains(
            ["example.com"],
            Resolved("example.com", "203.0.113.10"));
        var database = Database(
            (Sid, SettingsWithDomains("example.com")),
            (OtherSid, SettingsWithDomains("example.com")),
            ("S-1-5-21-default", new FirewallAccountSettings
            {
                Allowlist = [new FirewallAllowlistEntry { Value = "example.com", IsDomain = true }]
            }));

        cache.MarkDirtyForChangedDomains(database, changedDomains);

        Assert.True(cache.GetRefreshDecision(Sid, SettingsWithDomains("example.com"), changedDomains.ToHashSet(StringComparer.OrdinalIgnoreCase)).WasDirty);
        Assert.True(cache.GetRefreshDecision(OtherSid, SettingsWithDomains("example.com"), changedDomains.ToHashSet(StringComparer.OrdinalIgnoreCase)).WasDirty);
        Assert.False(cache.GetRefreshDecision("S-1-5-21-default", SettingsWithDomains("example.com"), EmptyChangedDomains()).WasDirty);
    }

    [Fact]
    public void EmptyDnsResult_PreservesPreviousCacheAndDirtyCanClearAfterSuccessfulRefresh()
    {
        var cache = new FirewallResolvedDomainCache();
        var settings = SettingsWithDomains("example.com");
        cache.UpdateResolvedDomainsAndGetChangedDomains(["example.com"], Resolved("example.com", "203.0.113.10"));
        cache.MarkDirty(Sid, ["example.com"]);

        var changed = cache.UpdateResolvedDomainsAndGetChangedDomains(
            ["example.com"],
            new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["example.com"] = []
            });
        var decision = cache.GetRefreshDecision(Sid, settings, changed.ToHashSet(StringComparer.OrdinalIgnoreCase));
        cache.MarkRefreshSucceeded(Sid, decision.DomainsToClearOnSuccess);

        Assert.Empty(changed);
        Assert.True(decision.ShouldRefreshRules);
        Assert.True(decision.WasDirty);
        Assert.Equal(["203.0.113.10"], decision.ResolvedDomains["example.com"]);
        Assert.Equal(["example.com"], decision.DomainsToClearOnSuccess);
        Assert.False(cache.GetRefreshDecision(Sid, settings, EmptyChangedDomains()).WasDirty);
    }

    [Fact]
    public void FirstTimeEmptyDnsResult_DoesNotMarkDirtyOrRefreshRules()
    {
        var cache = new FirewallResolvedDomainCache();
        var settings = SettingsWithDomains("example.com");

        var changed = cache.UpdateResolvedDomainsAndGetChangedDomains(
            ["example.com"],
            new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["example.com"] = []
            });
        var decision = cache.GetRefreshDecision(Sid, settings, changed.ToHashSet(StringComparer.OrdinalIgnoreCase));

        Assert.Empty(changed);
        Assert.False(decision.ShouldRefreshRules);
        Assert.Empty(decision.ResolvedDomains);
        Assert.Empty(decision.DomainsToClearOnSuccess);
    }

    [Fact]
    public void FirstTimeBlankDnsResult_DoesNotMarkDirtyOrStoreInvalidAddress()
    {
        var cache = new FirewallResolvedDomainCache();
        var settings = SettingsWithDomains("example.com");

        var changed = cache.UpdateResolvedDomainsAndGetChangedDomains(
            ["example.com"],
            new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["example.com"] = [" "]
            });
        var decision = cache.GetRefreshDecision(Sid, settings, changed.ToHashSet(StringComparer.OrdinalIgnoreCase));

        Assert.Empty(changed);
        Assert.False(decision.ShouldRefreshRules);
        Assert.Empty(cache.GetGlobalSnapshot());
        Assert.Empty(decision.ResolvedDomains);
    }

    [Fact]
    public void ResolvedAddresses_AreStoredWithoutBlankOrDuplicateValues()
    {
        var cache = new FirewallResolvedDomainCache();

        cache.UpdateResolvedDomainsAndGetChangedDomains(
            ["example.com"],
            new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["example.com"] = ["203.0.113.10", " ", "203.0.113.10"]
            });

        Assert.Equal(["203.0.113.10"], cache.GetGlobalSnapshot()["example.com"]);
    }

    [Fact]
    public void Prune_RemovesGlobalCachedDomainsOnlyWhenNoActiveAllowlistReferencesThem()
    {
        var cache = new FirewallResolvedDomainCache();
        cache.UpdateResolvedDomainsAndGetChangedDomains(
            ["shared.example", "remove.example"],
            new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["shared.example"] = ["203.0.113.10"],
                ["remove.example"] = ["203.0.113.11"]
            });

        cache.Prune(Database((Sid, SettingsWithDomains("shared.example"))));

        Assert.True(cache.GetGlobalSnapshot().ContainsKey("shared.example"));
        Assert.False(cache.GetGlobalSnapshot().ContainsKey("remove.example"));
    }

    [Fact]
    public void Prune_RemovesDirtyDomainsIndependentlyPerSid()
    {
        var cache = new FirewallResolvedDomainCache();
        cache.MarkDirty(Sid, ["keep.example", "remove.example"]);
        cache.MarkDirty(OtherSid, ["remove.example"]);

        cache.Prune(Database(
            (Sid, SettingsWithDomains("keep.example")),
            (OtherSid, SettingsWithDomains("remove.example"))));

        Assert.True(cache.GetRefreshDecision(Sid, SettingsWithDomains("keep.example"), EmptyChangedDomains()).WasDirty);
        Assert.False(cache.GetRefreshDecision(Sid, SettingsWithDomains("remove.example"), EmptyChangedDomains()).WasDirty);
        Assert.True(cache.GetRefreshDecision(OtherSid, SettingsWithDomains("remove.example"), EmptyChangedDomains()).WasDirty);
    }

    [Fact]
    public void Clear_ClearsGlobalCacheAndPerSidDirtyState()
    {
        var cache = new FirewallResolvedDomainCache();
        var settings = SettingsWithDomains("example.com");
        cache.UpdateResolvedDomainsAndGetChangedDomains(["example.com"], Resolved("example.com", "203.0.113.10"));
        cache.MarkDirty(Sid, ["example.com"]);

        cache.Clear();

        Assert.Empty(cache.GetGlobalSnapshot());
        Assert.Empty(cache.GetAccountSnapshot(settings));
        Assert.False(cache.GetRefreshDecision(Sid, settings, EmptyChangedDomains()).WasDirty);
    }

    private static Dictionary<string, List<string>> Resolved(string domain, string address) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            [domain] = [address]
        };

    private static FirewallAccountSettings SettingsWithDomains(params string[] domains) => new()
    {
        AllowInternet = false,
        AllowLan = true,
        Allowlist = domains
            .Select(domain => new FirewallAllowlistEntry { Value = domain, IsDomain = true })
            .ToList()
    };

    private static AppDatabase Database(params (string Sid, FirewallAccountSettings Settings)[] accounts)
    {
        var database = new AppDatabase();
        foreach (var account in accounts)
            database.GetOrCreateAccount(account.Sid).Firewall = account.Settings;

        return database;
    }

    private static IReadOnlySet<string> EmptyChangedDomains() => FirewallTestHelpers.EmptyChangedDomains();
}
