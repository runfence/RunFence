using RunFence.Core.Models;
using RunFence.Firewall;
using Xunit;

namespace RunFence.Tests;

public class FirewallDomainDirtyTrackerTests
{
    private const string Sid1 = "S-1-5-21-100-200-300-1001";
    private const string Sid2 = "S-1-5-21-100-200-300-1002";

    private readonly FirewallDomainDirtyTracker _tracker = new();

    // --- IsDefault entries filtered out (F-35) ---

    [Fact]
    public void MarkDirtyForChangedDomains_IsDefaultAccount_Skipped()
    {
        // Arrange: account whose firewall settings are all-allow defaults (IsDefault=true means
        // AllowInternet + AllowLocalhost + AllowLan all true, Allowlist empty).
        // IsEligibleForDomainAllowlistRefresh returns false for IsDefault accounts.
        var db = new AppDatabase();
        var account = db.GetOrCreateAccount(Sid1);
        account.Firewall = new FirewallAccountSettings
        {
            AllowInternet = true,
            AllowLocalhost = true,
            AllowLan = true,
            Allowlist = [] // IsDefault = true (computed)
        };

        // Act
        _tracker.MarkDirtyForChangedDomains(db, ["example.com"]);

        // Assert: IsDefault account is skipped — no dirty entries recorded
        Assert.False(_tracker.IsAnyDirty(Sid1, ["example.com"]));
    }

    // --- IsEligibleForDomainAllowlistRefresh with both SIDs allowed (F-35) ---

    [Fact]
    public void MarkDirtyForChangedDomains_EligibleAccounts_BothSidsMarkedDirty()
    {
        // Arrange: two eligible accounts each with a domain in their allowlist
        var db = new AppDatabase();

        var account1 = db.GetOrCreateAccount(Sid1);
        account1.Firewall = new FirewallAccountSettings
        {
            AllowInternet = false,
            AllowLan = false,
            Allowlist = [new FirewallAllowlistEntry { Value = "alpha.com", IsDomain = true }]
        };

        var account2 = db.GetOrCreateAccount(Sid2);
        account2.Firewall = new FirewallAccountSettings
        {
            AllowInternet = false,
            AllowLan = false,
            Allowlist = [new FirewallAllowlistEntry { Value = "beta.com", IsDomain = true }]
        };

        // Act: both domains change
        _tracker.MarkDirtyForChangedDomains(db, ["alpha.com", "beta.com"]);

        // Assert: both SIDs have dirty domains
        Assert.True(_tracker.IsAnyDirty(Sid1, ["alpha.com"]));
        Assert.True(_tracker.IsAnyDirty(Sid2, ["beta.com"]));
    }

    // --- PruneDirtyDomains removes SID (F-35) ---

    [Fact]
    public void PruneDirtyDomains_SidNotInActiveDomains_SidRemoved()
    {
        // Arrange: mark a domain dirty for Sid1, then prune with an active set that excludes Sid1
        _tracker.MarkDirty(Sid1, ["example.com"]);
        Assert.True(_tracker.IsAnyDirty(Sid1, ["example.com"]));

        // Prune: Sid1 is absent from active domains → all dirty entries for Sid1 removed
        var activeDomainsBySid = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        // Act
        _tracker.PruneDirtyDomains(activeDomainsBySid);

        // Assert: no dirty entries remain for Sid1
        Assert.False(_tracker.IsAnyDirty(Sid1, ["example.com"]));
    }

    [Fact]
    public void PruneDirtyDomains_DomainNotInActiveDomains_DomainRemoved()
    {
        // Arrange: Sid1 has two dirty domains; prune with active set containing only one
        _tracker.MarkDirty(Sid1, ["keep.com", "remove.com"]);

        var activeDomainsBySid = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [Sid1] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "keep.com" }
        };

        // Act
        _tracker.PruneDirtyDomains(activeDomainsBySid);

        // Assert: "keep.com" still dirty; "remove.com" pruned
        Assert.True(_tracker.IsAnyDirty(Sid1, ["keep.com"]));
        Assert.False(_tracker.IsAnyDirty(Sid1, ["remove.com"]));
    }
}
