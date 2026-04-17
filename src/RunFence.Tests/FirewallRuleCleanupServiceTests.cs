using Moq;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Firewall;
using RunFence.Firewall.Wfp;
using Xunit;

namespace RunFence.Tests;

public class FirewallRuleCleanupServiceTests
{
    private const string Sid = "S-1-5-21-1000-1000-1000-1001";
    private const string OrphanSid = "S-1-5-21-2000-2000-2000-2002";
    private const string StaleKnownSid = "S-1-5-21-3000-3000-3000-3003";

    private readonly Mock<ILoggingService> _log = new();
    private readonly Mock<IFirewallRuleManager> _ruleManager = new();
    private readonly Mock<IWfpLocalhostBlocker> _wfpBlocker = new();
    private readonly Mock<IWfpIcmpBlocker> _wfpIcmpBlocker = new();
    private readonly Mock<IGlobalIcmpPolicyService> _globalIcmpPolicy = new();
    private readonly FirewallResolvedDomainCache _domainCache = new();
    private readonly FirewallEnforcementRetryState _retryState = new();

    private FirewallRuleCleanupService BuildService() =>
        new(
            _log.Object,
            _ruleManager.Object,
            _wfpBlocker.Object,
            _wfpIcmpBlocker.Object,
            _globalIcmpPolicy.Object,
            _domainCache,
            _retryState);

    [Fact]
    public void RemoveAllRules_WhenWindowsFirewallEnumerationFails_StillCleansWfpBackends()
    {
        _ruleManager
            .Setup(r => r.GetRulesByGroup("RunFence"))
            .Throws(new InvalidOperationException("firewall unavailable"));

        var ex = Record.Exception(() => BuildService().RemoveAllRules(Sid));

        Assert.Null(ex);
        _wfpBlocker.Verify(
            w => w.Apply(Sid, false, It.Is<IReadOnlyList<string>>(ports => ports.Count == 0)),
            Times.Once);
        _wfpIcmpBlocker.Verify(w => w.Apply(Sid, false), Times.Once);
    }

    [Fact]
    public void RemoveAll_WhenBackendsFail_StillAttemptsWfpForAllKnownSidsAndGlobalIcmp()
    {
        var orphanRule = MakeRule("RunFence Block Internet IPv4 (orphan)", OrphanSid);
        var database = new AppDatabase
        {
            SidNames =
            {
                [Sid] = "alice",
                [StaleKnownSid] = "stale"
            }
        };
        _ruleManager
            .Setup(r => r.GetRulesByGroup("RunFence"))
            .Returns([orphanRule]);
        _ruleManager
            .Setup(r => r.RemoveRule(orphanRule.Name))
            .Throws(new InvalidOperationException("remove failed"));
        _wfpBlocker
            .Setup(w => w.Apply(Sid, false, It.IsAny<IReadOnlyList<string>>()))
            .Throws(new InvalidOperationException("localhost cleanup failed"));
        _wfpIcmpBlocker
            .Setup(w => w.Apply(Sid, false))
            .Throws(new InvalidOperationException("icmp cleanup failed"));
        _globalIcmpPolicy
            .Setup(g => g.RemoveGlobalIcmpBlock())
            .Throws(new InvalidOperationException("global cleanup failed"));

        var ex = Record.Exception(() => BuildService().RemoveAll(database));

        Assert.Null(ex);
        _ruleManager.Verify(r => r.RemoveRule(orphanRule.Name), Times.Once);
        _wfpBlocker.Verify(
            w => w.Apply(Sid, false, It.Is<IReadOnlyList<string>>(ports => ports.Count == 0)),
            Times.Once);
        _wfpBlocker.Verify(
            w => w.Apply(OrphanSid, false, It.Is<IReadOnlyList<string>>(ports => ports.Count == 0)),
            Times.Once);
        _wfpBlocker.Verify(
            w => w.Apply(StaleKnownSid, false, It.Is<IReadOnlyList<string>>(ports => ports.Count == 0)),
            Times.Once);
        _wfpIcmpBlocker.Verify(w => w.Apply(Sid, false), Times.Once);
        _wfpIcmpBlocker.Verify(w => w.Apply(OrphanSid, false), Times.Once);
        _wfpIcmpBlocker.Verify(w => w.Apply(StaleKnownSid, false), Times.Once);
        _globalIcmpPolicy.Verify(g => g.RemoveGlobalIcmpBlock(), Times.Once);
    }

    [Fact]
    public void RemoveAll_ClearsDomainCacheAndRetryState()
    {
        var database = new AppDatabase
        {
            SidNames =
            {
                [Sid] = "alice"
            }
        };
        _ruleManager
            .Setup(r => r.GetRulesByGroup("RunFence"))
            .Returns([]);
        _domainCache.UpdateResolvedDomainsAndGetChangedDomains(
            ["example.com"],
            new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["example.com"] = ["203.0.113.10"]
            });
        _domainCache.MarkDirty(Sid, ["example.com"]);
        _retryState.UpdateDnsServersAndReturnChanged(["192.0.2.53"]);
        _retryState.MarkDnsServerRefreshPending([Sid]);
        _retryState.MarkGlobalIcmpDirty();

        BuildService().RemoveAll(database);

        Assert.Empty(_domainCache.GetAccountSnapshot(new FirewallAccountSettings
        {
            AllowInternet = false,
            Allowlist = [new FirewallAllowlistEntry { Value = "example.com", IsDomain = true }]
        }));
        Assert.Empty(_domainCache.GetGlobalSnapshot());
        Assert.Empty(_retryState.GetDnsServerRefreshPendingSids());
        Assert.False(_retryState.IsGlobalIcmpDirty());
        Assert.True(_retryState.UpdateDnsServersAndReturnChanged(["192.0.2.53"]));
    }

    [Fact]
    public void RemoveAll_WhenWindowsFirewallEnumerationFails_StillCleansKnownSidsAndGlobalIcmp()
    {
        var database = new AppDatabase
        {
            SidNames =
            {
                [Sid] = "alice"
            }
        };
        _ruleManager
            .Setup(r => r.GetRulesByGroup("RunFence"))
            .Throws(new InvalidOperationException("firewall unavailable"));

        var ex = Record.Exception(() => BuildService().RemoveAll(database));

        Assert.Null(ex);
        _wfpBlocker.Verify(
            w => w.Apply(Sid, false, It.Is<IReadOnlyList<string>>(ports => ports.Count == 0)),
            Times.Once);
        _wfpIcmpBlocker.Verify(w => w.Apply(Sid, false), Times.Once);
        _globalIcmpPolicy.Verify(g => g.RemoveGlobalIcmpBlock(), Times.Once);
    }

    [Fact]
    public void CleanupOrphanedRules_RemovesOrphanRulesAndCleansKnownInactiveWfpOnlySid()
    {
        var activeRule = MakeRule("RunFence Block Internet IPv4 (alice)", Sid);
        var orphanRule = MakeRule("RunFence Block Internet IPv4 (orphan)", OrphanSid);
        _ruleManager
            .Setup(r => r.GetRulesByGroup("RunFence"))
            .Returns([activeRule, orphanRule]);

        BuildService().CleanupOrphanedRules(
            new HashSet<string>([Sid], StringComparer.OrdinalIgnoreCase),
            [Sid, OrphanSid, StaleKnownSid]);

        _ruleManager.Verify(r => r.RemoveRule(activeRule.Name), Times.Never);
        _ruleManager.Verify(r => r.RemoveRule(orphanRule.Name), Times.Once);
        _wfpBlocker.Verify(
            w => w.Apply(OrphanSid, false, It.Is<IReadOnlyList<string>>(ports => ports.Count == 0)),
            Times.Once);
        _wfpBlocker.Verify(
            w => w.Apply(StaleKnownSid, false, It.Is<IReadOnlyList<string>>(ports => ports.Count == 0)),
            Times.Once);
        _wfpIcmpBlocker.Verify(w => w.Apply(OrphanSid, false), Times.Once);
        _wfpIcmpBlocker.Verify(w => w.Apply(StaleKnownSid, false), Times.Once);
        _wfpBlocker.Verify(
            w => w.Apply(Sid, false, It.IsAny<IReadOnlyList<string>>()),
            Times.Never);
        _wfpIcmpBlocker.Verify(w => w.Apply(Sid, false), Times.Never);
    }

    private static FirewallRuleInfo MakeRule(string name, string sid) =>
        new(
            Name: name,
            LocalUser: $"D:(A;;CC;;;{sid})",
            RemoteAddress: "1.2.3.4",
            Direction: 2,
            Action: 0,
            Protocol: 256,
            Grouping: "RunFence",
            Description: "Managed by RunFence");
}
