using Moq;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Firewall;
using RunFence.Firewall.Wfp;
using Xunit;

namespace RunFence.Tests;

public class FirewallEnforcementOrchestratorTests
{
    private const string Sid = "S-1-5-21-1000-1000-1000-1001";
    private const string Username = "alice";

    private readonly Mock<ILoggingService> _log = new();
    private readonly Mock<IFirewallRuleManager> _ruleManager = new();
    private readonly Mock<IWfpLocalhostBlocker> _wfpBlocker = new();
    private readonly Mock<IWfpIcmpBlocker> _wfpIcmpBlocker = new();
    private readonly Mock<INetworkInterfaceInfoProvider> _networkInfo = new();
    private readonly Mock<IGlobalIcmpPolicyService> _globalIcmpPolicy = new();
    private readonly Mock<IFirewallCleanupService> _cleanupService = new();
    private readonly Mock<IFirewallDomainRefreshRequester> _refreshRequester = new();

    private readonly FirewallResolvedDomainCache _domainCache = new();
    private readonly FirewallEnforcementRetryState _retryState = new();

    public FirewallEnforcementOrchestratorTests()
    {
        _ruleManager.Setup(r => r.GetRulesByGroup("RunFence")).Returns([]);
        _networkInfo.Setup(n => n.GetDnsServerAddresses()).Returns(["192.0.2.53"]);
        _networkInfo.Setup(n => n.GetLocalAddresses()).Returns([]);
    }

    [Fact]
    public void ApplyAccountFirewallSettings_WithMissingDomain_MarksDirtyRequestsRefreshAndEnforcesGlobalIcmp()
    {
        var orchestrator = BuildOrchestrator();
        var settings = BlockInternetWithDomain("example.com");
        var database = Database(settings);

        var result = orchestrator.ApplyAccountFirewallSettings(Sid, Username, new FirewallAccountSettings(), settings, database);
        var dirtyDecision = _domainCache.GetRefreshDecision(Sid, settings, EmptyChangedDomains());

        Assert.True(result.AccountRulesApplied);
        Assert.True(result.GlobalIcmpApplied);
        Assert.Equal([new FirewallPendingDomainResolution(Sid, "example.com")], result.PendingDomains);
        Assert.True(dirtyDecision.WasDirty);
        _refreshRequester.Verify(r => r.RequestRefresh(), Times.Once);
        _globalIcmpPolicy.Verify(g => g.EnforceGlobalIcmpBlock(database, It.IsAny<IReadOnlyDictionary<string, IReadOnlyList<string>>>()), Times.Once);
    }

    [Fact]
    public void ApplyAccountFirewallSettings_UsesCachedGlobalDomainValuesFilteredBySettings()
    {
        var orchestrator = BuildOrchestrator();
        var settings = BlockInternetWithDomain("example.com");
        var database = Database(settings);
        _domainCache.UpdateResolvedDomainsAndGetChangedDomains(
            ["example.com", "other.example"],
            new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["example.com"] = ["203.0.113.10"],
                ["other.example"] = ["203.0.113.11"]
            });

        var result = orchestrator.ApplyAccountFirewallSettings(Sid, Username, new FirewallAccountSettings(), settings, database);

        Assert.Empty(result.PendingDomains);
        _refreshRequester.Verify(r => r.RequestRefresh(), Times.Never);
    }

    [Fact]
    public void ApplyAccountFirewallSettings_WhenGlobalIcmpFails_DoesNotRollbackAccountArtifactsAndMarksRetryDirty()
    {
        var orchestrator = BuildOrchestrator();
        var settings = BlockInternetWithDomain("example.com");
        var database = Database(settings);
        _globalIcmpPolicy
            .Setup(g => g.EnforceGlobalIcmpBlock(database, It.IsAny<IReadOnlyDictionary<string, IReadOnlyList<string>>>()))
            .Throws(new InvalidOperationException("global ICMP failed"));

        var ex = Assert.Throws<FirewallApplyException>(() =>
            orchestrator.ApplyAccountFirewallSettings(Sid, Username, new FirewallAccountSettings(), settings, database));

        Assert.Equal(FirewallApplyPhase.GlobalIcmp, ex.Phase);
        Assert.True(_retryState.IsGlobalIcmpDirty());
        _wfpIcmpBlocker.Verify(w => w.Apply(Sid, true), Times.Once);
        _wfpIcmpBlocker.Verify(w => w.Apply(Sid, false), Times.Never);
        _refreshRequester.Verify(r => r.RequestRefresh(), Times.Exactly(2));
    }

    [Fact]
    public async Task ApplyAccountFirewallSettingsAsync_CreatesGlobalPlanBeforeWorkerApply()
    {
        var orchestrator = BuildOrchestrator();
        var settings = BlockInternetWithDomain("example.com");
        var database = Database(settings);
        var plan = new GlobalIcmpEnforcementPlan(true, 1, ["203.0.113.10"]);
        GlobalIcmpPolicyInput? capturedGlobalIcmpInput = null;
        _globalIcmpPolicy
            .Setup(g => g.CreateGlobalIcmpPlan(
                It.IsAny<GlobalIcmpPolicyInput>(),
                It.IsAny<IReadOnlyDictionary<string, IReadOnlyList<string>>>()))
            .Callback<GlobalIcmpPolicyInput, IReadOnlyDictionary<string, IReadOnlyList<string>>>(
                (input, _) => capturedGlobalIcmpInput = input)
            .Returns(plan);

        var result = await orchestrator.ApplyAccountFirewallSettingsAsync(
            Sid,
            Username,
            new FirewallAccountSettings(),
            settings,
            database);

        Assert.True(result.GlobalIcmpApplied);
        Assert.NotNull(capturedGlobalIcmpInput);
        Assert.True(capturedGlobalIcmpInput.BlockIcmpWhenInternetBlocked);
        Assert.Single(capturedGlobalIcmpInput.BlockedAccounts);
        Assert.Equal(Sid, capturedGlobalIcmpInput.BlockedAccounts[0].Sid);
        Assert.NotSame(database.Accounts[0], capturedGlobalIcmpInput.BlockedAccounts[0]);
        _globalIcmpPolicy.Verify(g => g.CreateGlobalIcmpPlan(
            It.IsAny<GlobalIcmpPolicyInput>(),
            It.IsAny<IReadOnlyDictionary<string, IReadOnlyList<string>>>()), Times.Once);
        _globalIcmpPolicy.Verify(g => g.EnforceGlobalIcmpBlock(plan), Times.Once);
        _globalIcmpPolicy.Verify(g => g.EnforceGlobalIcmpBlock(
            It.IsAny<AppDatabase>(),
            It.IsAny<IReadOnlyDictionary<string, IReadOnlyList<string>>>()), Times.Never);
    }

    [Fact]
    public async Task ApplyAccountFirewallSettingsAsync_GlobalIcmpFailureMarksRetryDirty()
    {
        var orchestrator = BuildOrchestrator();
        var settings = BlockInternetWithDomain("example.com");
        var database = Database(settings);
        var plan = new GlobalIcmpEnforcementPlan(true, 1, []);
        _globalIcmpPolicy
            .Setup(g => g.CreateGlobalIcmpPlan(
                It.IsAny<GlobalIcmpPolicyInput>(),
                It.IsAny<IReadOnlyDictionary<string, IReadOnlyList<string>>>()))
            .Returns(plan);
        _globalIcmpPolicy
            .Setup(g => g.EnforceGlobalIcmpBlock(plan))
            .Throws(new InvalidOperationException("global ICMP failed"));

        var ex = await Assert.ThrowsAsync<FirewallApplyException>(() =>
            orchestrator.ApplyAccountFirewallSettingsAsync(
                Sid,
                Username,
                new FirewallAccountSettings(),
                settings,
                database));

        Assert.Equal(FirewallApplyPhase.GlobalIcmp, ex.Phase);
        Assert.True(_retryState.IsGlobalIcmpDirty());
        _refreshRequester.Verify(r => r.RequestRefresh(), Times.Exactly(2));
    }

    [Fact]
    public void ApplyGlobalIcmpSetting_WithMissingCommonDomain_MarksDirtyRequestsRefreshAndDoesNotResolveDns()
    {
        var globalIcmpOrchestrator = BuildGlobalIcmpOrchestrator();
        var settings = BlockInternetWithDomain("example.com");
        var database = Database(settings);

        globalIcmpOrchestrator.ApplyGlobalIcmpSetting(database);
        var dirtyDecision = _domainCache.GetRefreshDecision(Sid, settings, EmptyChangedDomains());

        Assert.True(dirtyDecision.WasDirty);
        _refreshRequester.Verify(r => r.RequestRefresh(), Times.Once);
        _globalIcmpPolicy.Verify(g => g.EnforceGlobalIcmpBlock(database, It.IsAny<IReadOnlyDictionary<string, IReadOnlyList<string>>>()), Times.Once);
    }

    [Fact]
    public void ApplyGlobalIcmpSetting_PrunesRemovedDomainsBeforeReadingGlobalSnapshot()
    {
        var globalIcmpOrchestrator = BuildGlobalIcmpOrchestrator();
        _domainCache.UpdateResolvedDomainsAndGetChangedDomains(
            ["removed.example"],
            new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["removed.example"] = ["203.0.113.10"]
            });
        var database = new AppDatabase();

        globalIcmpOrchestrator.ApplyGlobalIcmpSetting(database);

        _globalIcmpPolicy.Verify(g => g.EnforceGlobalIcmpBlock(
            database,
            It.Is<IReadOnlyDictionary<string, IReadOnlyList<string>>>(snapshot => snapshot.Count == 0)), Times.Once);
    }

    [Fact]
    public void EnforceAll_LogsAccountFailuresContinuesCleanupAndMarksGlobalIcmpDirtyOnFailure()
    {
        var orchestrator = BuildOrchestrator();
        var settings = BlockInternetWithDomain("example.com");
        var database = Database(settings);
        _ruleManager
            .Setup(r => r.GetRulesByGroup("RunFence"))
            .Throws(new InvalidOperationException("firewall unavailable"));
        _globalIcmpPolicy
            .Setup(g => g.EnforceGlobalIcmpBlock(database, It.IsAny<IReadOnlyDictionary<string, IReadOnlyList<string>>>()))
            .Throws(new InvalidOperationException("global ICMP failed"));

        orchestrator.EnforceAll(database);

        Assert.True(_retryState.IsGlobalIcmpDirty());
        _cleanupService.Verify(c => c.CleanupOrphanedRules(
                It.Is<IReadOnlySet<string>>(sids => sids.Contains(Sid)),
                It.Is<IEnumerable<string>>(sids => sids.Contains(Sid))),
            Times.Once);
        _refreshRequester.Verify(r => r.RequestRefresh(), Times.Once);
        _log.Verify(l => l.Error(
                It.Is<string>(message => message.Contains("Failed to apply rules", StringComparison.Ordinal)),
                It.IsAny<Exception>()),
            Times.Once);
    }

    private GlobalIcmpEnforcementOrchestrator BuildGlobalIcmpOrchestrator()
    {
        var addressBuilder = new FirewallAddressExclusionBuilder(new FirewallAddressRangeBuilder(), _networkInfo.Object);
        return new GlobalIcmpEnforcementOrchestrator(
            _domainCache,
            _retryState,
            addressBuilder,
            _globalIcmpPolicy.Object,
            _refreshRequester.Object);
    }

    private FirewallEnforcementOrchestrator BuildOrchestrator()
    {
        var addressBuilder = new FirewallAddressExclusionBuilder(new FirewallAddressRangeBuilder(), _networkInfo.Object);
        var comApplier = new FirewallComRuleApplier(_ruleManager.Object, addressBuilder, _log.Object);
        var wfpApplier = new FirewallWfpRuleApplier(_wfpBlocker.Object, _wfpIcmpBlocker.Object, _log.Object);
        var accountRuleApplier = new FirewallAccountRuleApplier(comApplier, wfpApplier);
        var globalIcmpOrchestrator = new GlobalIcmpEnforcementOrchestrator(
            _domainCache,
            _retryState,
            addressBuilder,
            _globalIcmpPolicy.Object,
            _refreshRequester.Object);
        return new FirewallEnforcementOrchestrator(
            _log.Object,
            _domainCache,
            _retryState,
            accountRuleApplier,
            _cleanupService.Object,
            globalIcmpOrchestrator,
            globalIcmpOrchestrator);
    }

    private static FirewallAccountSettings BlockInternetWithDomain(string domain) => new()
    {
        AllowInternet = false,
        AllowLocalhost = true,
        AllowLan = true,
        Allowlist = [new FirewallAllowlistEntry { Value = domain, IsDomain = true }]
    };

    private static AppDatabase Database(FirewallAccountSettings settings) => new()
    {
        Accounts =
        [
            new AccountEntry
            {
                Sid = Sid,
                Firewall = settings
            }
        ],
        SidNames =
        {
            [Sid] = Username
        }
    };

    private static IReadOnlySet<string> EmptyChangedDomains() => FirewallTestHelpers.EmptyChangedDomains();
}
