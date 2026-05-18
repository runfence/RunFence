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
    private const string Sid2 = "S-1-5-21-1000-1000-1000-1002";
    private const string Username = "alice";

    private readonly Mock<ILoggingService> _log = new();
    private readonly FirewallResolvedDomainCache _domainCache = new(new FirewallDomainDirtyTracker());
    private readonly FirewallEnforcementRetryState _retryState = new();
    private readonly Mock<IFirewallAccountRuleApplier> _accountRuleApplier = new();
    private readonly Mock<IFirewallCleanupService> _cleanupService = new();
    private readonly Mock<IGlobalIcmpPendingDomainProcessor> _pendingDomainProcessor = new();
    private readonly Mock<IGlobalIcmpEnforcementTrigger> _globalIcmpTrigger = new();
    private readonly Mock<IFirewallDomainRefreshRequester> _refreshRequester = new();

    [Fact]
    public void ApplyAccountFirewallSettings_WfpFailure_ReturnsFailedEntryAndSchedulesRetry()
    {
        var database = Database(new FirewallAccountSettings());
        var finalSettings = new FirewallAccountSettings { AllowLocalhost = false };
        _accountRuleApplier
            .Setup(a => a.ApplyFirewallRules(
                Sid,
                Username,
                It.IsAny<FirewallAccountSettings>(),
                It.IsAny<FirewallAccountSettings?>(),
                It.IsAny<IReadOnlyDictionary<string, IReadOnlyList<string>>>()))
            .Returns(new FirewallAccountRuleApplyResult(
                false,
                [],
                FirewallEnforcementLayer.WfpFilters,
                "wfp failed"));

        var result = CreateOrchestrator().ApplyAccountFirewallSettings(
            Sid,
            Username,
            new FirewallAccountSettings(),
            finalSettings,
            database);

        Assert.True(result.HasBlockingFailure);
        Assert.Contains(result.EnforcementEntries, entry =>
            entry.Layer == FirewallEnforcementLayer.WfpFilters
            && entry.Status == FirewallEnforcementStatus.Failed
            && entry.Error == "wfp failed");
        Assert.Contains(_retryState.GetRetryEntries(), entry => entry.Layer == FirewallEnforcementLayer.AccountRules && entry.Key == Sid);
        Assert.Contains(_retryState.GetRetryEntries(), entry => entry.Layer == FirewallEnforcementLayer.WfpFilters && entry.Key == Sid);
        _refreshRequester.Verify(r => r.RequestRefresh(), Times.Once);
    }

    [Fact]
    public void ApplyAccountFirewallSettings_ApplyFirewallCoreThrowsAccountRuleFailure_ReturnsFailedEntryAndSchedulesRetry()
    {
        var database = Database(new FirewallAccountSettings());
        var finalSettings = new FirewallAccountSettings { AllowLocalhost = false };
        var calls = new List<string>();

        _accountRuleApplier
            .Setup(a => a.ApplyFirewallRules(
                Sid,
                Username,
                It.IsAny<FirewallAccountSettings>(),
                It.IsAny<FirewallAccountSettings?>(),
                It.IsAny<IReadOnlyDictionary<string, IReadOnlyList<string>>>()))
            .Callback(() => calls.Add("apply"))
            .Throws(new InvalidOperationException("account rules failed"));

        var result = CreateOrchestrator().ApplyAccountFirewallSettings(
            Sid,
            Username,
            new FirewallAccountSettings(),
            finalSettings,
            database,
            saveAction: () => calls.Add("save"));

        Assert.True(result.HasBlockingFailure);
        Assert.Contains(result.EnforcementEntries, entry =>
            entry.Layer == FirewallEnforcementLayer.AccountRules
            && entry.Status == FirewallEnforcementStatus.Failed
            && entry.Error == "account rules failed");
        Assert.Contains(_retryState.GetRetryEntries(), entry => entry.Layer == FirewallEnforcementLayer.AccountRules && entry.Key == Sid);
        Assert.Contains(_retryState.GetRetryEntries(), entry => entry.Layer == FirewallEnforcementLayer.WfpFilters && entry.Key == Sid);
        Assert.True(result.ConfigSaved);
        Assert.Equal(["save", "apply"], calls);
        _refreshRequester.Verify(r => r.RequestRefresh(), Times.Once);
    }

    [Fact]
    public void ApplyAccountFirewallSettings_ApplyFirewallCoreThrowsWfpFailure_ReturnsFailedEntryAndSchedulesRetry()
    {
        var database = Database(new FirewallAccountSettings());
        var finalSettings = new FirewallAccountSettings { AllowLocalhost = false };
        var calls = new List<string>();

        _accountRuleApplier
            .Setup(a => a.ApplyFirewallRules(
                Sid,
                Username,
                It.IsAny<FirewallAccountSettings>(),
                It.IsAny<FirewallAccountSettings?>(),
                It.IsAny<IReadOnlyDictionary<string, IReadOnlyList<string>>>()))
            .Throws(new WfpFilterHelperException("wfp failed"));

        var result = CreateOrchestrator().ApplyAccountFirewallSettings(
            Sid,
            Username,
            new FirewallAccountSettings(),
            finalSettings,
            database,
            saveAction: () => calls.Add("save"));

        Assert.True(result.HasBlockingFailure);
        Assert.Contains(result.EnforcementEntries, entry =>
            entry.Layer == FirewallEnforcementLayer.WfpFilters
            && entry.Status == FirewallEnforcementStatus.Failed
            && entry.Error == "wfp failed");
        Assert.Contains(_retryState.GetRetryEntries(), entry => entry.Layer == FirewallEnforcementLayer.AccountRules && entry.Key == Sid);
        Assert.Contains(_retryState.GetRetryEntries(), entry => entry.Layer == FirewallEnforcementLayer.WfpFilters && entry.Key == Sid);
        Assert.True(result.ConfigSaved);
        Assert.Equal(["save"], calls);
        _refreshRequester.Verify(r => r.RequestRefresh(), Times.Once);
    }

    [Fact]
    public void ApplyAccountFirewallSettings_RemoveOrLoosen_ApplyFirewallCoreThrowsFailure_ReportsConfigNotSaved()
    {
        var previous = new FirewallAccountSettings { AllowInternet = false };
        var final = new FirewallAccountSettings { AllowInternet = true };
        var database = Database(previous.Clone());
        var calls = new List<string>();

        _accountRuleApplier
            .Setup(a => a.ApplyFirewallRules(
                Sid,
                Username,
                It.IsAny<FirewallAccountSettings>(),
                It.IsAny<FirewallAccountSettings?>(),
                It.IsAny<IReadOnlyDictionary<string, IReadOnlyList<string>>>()))
            .Callback(() => calls.Add("apply"))
            .Throws(new InvalidOperationException("remove tighten failed"));

        var result = CreateOrchestrator().ApplyAccountFirewallSettings(
            Sid,
            Username,
            previous,
            final,
            database,
            saveAction: () => calls.Add("save"));

        Assert.True(result.HasBlockingFailure);
        Assert.Contains(result.EnforcementEntries, entry =>
            entry.Layer == FirewallEnforcementLayer.AccountRules
            && entry.Status == FirewallEnforcementStatus.Failed
            && entry.Error == "remove tighten failed");
        Assert.False(result.ConfigSaved);
        Assert.Equal(["apply"], calls);
        Assert.False(database.GetAccount(Sid)!.Firewall.AllowInternet);
        _refreshRequester.Verify(r => r.RequestRefresh(), Times.Once);
    }

    [Fact]
    public async Task ApplyAccountFirewallSettingsAsync_ApplyFirewallCoreThrowsAccountRuleFailure_ReturnsFailedEntryAndSchedulesRetry()
    {
        var database = Database(new FirewallAccountSettings());
        var finalSettings = new FirewallAccountSettings { AllowLocalhost = false };
        var calls = new List<string>();

        _accountRuleApplier
            .Setup(a => a.ApplyFirewallRules(
                Sid,
                Username,
                It.IsAny<FirewallAccountSettings>(),
                It.IsAny<FirewallAccountSettings?>(),
                It.IsAny<IReadOnlyDictionary<string, IReadOnlyList<string>>>()))
            .Callback(() => calls.Add("apply"))
            .Throws(new InvalidOperationException("account rules failed"));

        var result = await CreateOrchestrator().ApplyAccountFirewallSettingsAsync(
            Sid,
            Username,
            new FirewallAccountSettings(),
            finalSettings,
            database,
            saveAction: () => calls.Add("save"));

        Assert.True(result.HasBlockingFailure);
        Assert.Contains(result.EnforcementEntries, entry =>
            entry.Layer == FirewallEnforcementLayer.AccountRules
            && entry.Status == FirewallEnforcementStatus.Failed
            && entry.Error == "account rules failed");
        Assert.Contains(_retryState.GetRetryEntries(), entry => entry.Layer == FirewallEnforcementLayer.AccountRules && entry.Key == Sid);
        Assert.Contains(_retryState.GetRetryEntries(), entry => entry.Layer == FirewallEnforcementLayer.WfpFilters && entry.Key == Sid);
        Assert.True(result.ConfigSaved);
        Assert.Equal(["save", "apply"], calls);
        _refreshRequester.Verify(r => r.RequestRefresh(), Times.Once);
    }

    [Fact]
    public async Task ApplyAccountFirewallSettingsAsync_ApplyFirewallCoreThrowsWfpFailure_ReturnsFailedEntryAndSchedulesRetry()
    {
        var database = Database(new FirewallAccountSettings());
        var finalSettings = new FirewallAccountSettings { AllowLocalhost = false };
        var calls = new List<string>();

        _accountRuleApplier
            .Setup(a => a.ApplyFirewallRules(
                Sid,
                Username,
                It.IsAny<FirewallAccountSettings>(),
                It.IsAny<FirewallAccountSettings?>(),
                It.IsAny<IReadOnlyDictionary<string, IReadOnlyList<string>>>()))
            .Throws(new WfpFilterHelperException("wfp failed"));

        var result = await CreateOrchestrator().ApplyAccountFirewallSettingsAsync(
            Sid,
            Username,
            new FirewallAccountSettings(),
            finalSettings,
            database,
            saveAction: () => calls.Add("save"));

        Assert.True(result.HasBlockingFailure);
        Assert.Contains(result.EnforcementEntries, entry =>
            entry.Layer == FirewallEnforcementLayer.WfpFilters
            && entry.Status == FirewallEnforcementStatus.Failed
            && entry.Error == "wfp failed");
        Assert.Contains(_retryState.GetRetryEntries(), entry => entry.Layer == FirewallEnforcementLayer.AccountRules && entry.Key == Sid);
        Assert.Contains(_retryState.GetRetryEntries(), entry => entry.Layer == FirewallEnforcementLayer.WfpFilters && entry.Key == Sid);
        Assert.True(result.ConfigSaved);
        Assert.Equal(["save"], calls);
        _refreshRequester.Verify(r => r.RequestRefresh(), Times.Once);
    }

    [Fact]
    public async Task ApplyAccountFirewallSettingsAsync_RemoveOrLoosen_ApplyFirewallCoreThrowsFailure_ReportsConfigNotSaved()
    {
        var previous = new FirewallAccountSettings { AllowInternet = false };
        var final = new FirewallAccountSettings { AllowInternet = true };
        var database = Database(previous.Clone());
        var calls = new List<string>();

        _accountRuleApplier
            .Setup(a => a.ApplyFirewallRules(
                Sid,
                Username,
                It.IsAny<FirewallAccountSettings>(),
                It.IsAny<FirewallAccountSettings?>(),
                It.IsAny<IReadOnlyDictionary<string, IReadOnlyList<string>>>()))
            .Callback(() => calls.Add("apply"))
            .Throws(new InvalidOperationException("remove tighten failed"));

        var result = await CreateOrchestrator().ApplyAccountFirewallSettingsAsync(
            Sid,
            Username,
            previous,
            final,
            database,
            saveAction: () => calls.Add("save"));

        Assert.True(result.HasBlockingFailure);
        Assert.Contains(result.EnforcementEntries, entry =>
            entry.Layer == FirewallEnforcementLayer.AccountRules
            && entry.Status == FirewallEnforcementStatus.Failed
            && entry.Error == "remove tighten failed");
        Assert.Equal(["apply"], calls);
        Assert.False(result.ConfigSaved);
        Assert.False(database.GetAccount(Sid)!.Firewall.AllowInternet);
        _refreshRequester.Verify(r => r.RequestRefresh(), Times.Once);
    }

    [Fact]
    public async Task ApplyAccountFirewallSettingsAsync_ApplyFirewallCoreThrowsOperationCanceledException_ConvertsToFailure()
    {
        var database = Database(new FirewallAccountSettings());
        var finalSettings = new FirewallAccountSettings { AllowLocalhost = false };

        _accountRuleApplier
            .Setup(a => a.ApplyFirewallRules(
                Sid,
                Username,
                It.IsAny<FirewallAccountSettings>(),
                It.IsAny<FirewallAccountSettings?>(),
                It.IsAny<IReadOnlyDictionary<string, IReadOnlyList<string>>>()))
            .Throws(new OperationCanceledException("not canceled"));

        using var cancellationSource = new CancellationTokenSource();
        var result = await CreateOrchestrator().ApplyAccountFirewallSettingsAsync(
            Sid,
            Username,
            new FirewallAccountSettings(),
            finalSettings,
            database,
            cancellationToken: cancellationSource.Token);

        Assert.True(result.HasBlockingFailure);
        Assert.Contains(result.EnforcementEntries, entry =>
            entry.Layer == FirewallEnforcementLayer.AccountRules
            && entry.Status == FirewallEnforcementStatus.Failed
            && entry.Error == "not canceled");
        _refreshRequester.Verify(r => r.RequestRefresh(), Times.Once);
    }

    [Fact]
    public async Task ApplyAccountFirewallSettingsAsync_CancelledToken_RethrowsOperationCanceledException()
    {
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        var database = Database(new FirewallAccountSettings());
        var finalSettings = new FirewallAccountSettings { AllowLocalhost = false };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CreateOrchestrator().ApplyAccountFirewallSettingsAsync(
                Sid,
                Username,
                new FirewallAccountSettings(),
                finalSettings,
                database,
                cancellationToken: cancellationSource.Token));
    }

    [Fact]
    public void ApplyAccountFirewallSettings_GlobalIcmpFailure_ReturnsRetryWarning()
    {
        var database = Database(new FirewallAccountSettings { AllowInternet = false });
        _accountRuleApplier
            .Setup(a => a.ApplyFirewallRules(
                Sid,
                Username,
                It.IsAny<FirewallAccountSettings>(),
                It.IsAny<FirewallAccountSettings?>(),
                It.IsAny<IReadOnlyDictionary<string, IReadOnlyList<string>>>()))
            .Returns(new FirewallAccountRuleApplyResult(true, []));
        _globalIcmpTrigger
            .Setup(g => g.EnforceGlobalIcmpBlock(It.IsAny<AppDatabase>()))
            .Throws(new InvalidOperationException("global icmp failed"));

        var result = CreateOrchestrator().ApplyAccountFirewallSettings(
            Sid,
            Username,
            new FirewallAccountSettings(),
            new FirewallAccountSettings { AllowInternet = false },
            database);

        Assert.Contains(result.EnforcementEntries, entry =>
            entry.Layer == FirewallEnforcementLayer.GlobalIcmp
            && entry.Status == FirewallEnforcementStatus.RetryScheduled);
    }

    [Fact]
    public void ApplyAccountFirewallSettings_GlobalIcmpFailure_SchedulesRetryFromPhaseResult()
    {
        var database = Database(new FirewallAccountSettings { AllowInternet = false });
        _accountRuleApplier
            .Setup(a => a.ApplyFirewallRules(
                Sid,
                Username,
                It.IsAny<FirewallAccountSettings>(),
                It.IsAny<FirewallAccountSettings?>(),
                It.IsAny<IReadOnlyDictionary<string, IReadOnlyList<string>>>()))
            .Returns(new FirewallAccountRuleApplyResult(true, []));
        _globalIcmpTrigger
            .Setup(g => g.EnforceGlobalIcmpBlock(It.IsAny<AppDatabase>()))
            .Throws(new InvalidOperationException("global icmp failed"));

        CreateOrchestrator().ApplyAccountFirewallSettings(
            Sid,
            Username,
            new FirewallAccountSettings(),
            new FirewallAccountSettings { AllowInternet = false },
            database);

        Assert.True(_retryState.IsGlobalIcmpDirty());
    }

    [Fact]
    public void ApplyAccountFirewallSettings_RemoveOrLoosenGlobalIcmpFailure_SavesRequestedSettingsAndSchedulesRetry()
    {
        var previous = new FirewallAccountSettings { AllowInternet = false };
        var final = new FirewallAccountSettings { AllowInternet = true };
        var database = Database(previous.Clone());
        var calls = new List<string>();

        _accountRuleApplier
            .Setup(a => a.ApplyFirewallRules(
                Sid,
                Username,
                It.IsAny<FirewallAccountSettings>(),
                It.IsAny<FirewallAccountSettings?>(),
                It.IsAny<IReadOnlyDictionary<string, IReadOnlyList<string>>>()))
            .Callback(() => calls.Add("account"))
            .Returns(new FirewallAccountRuleApplyResult(true, []));
        _globalIcmpTrigger
            .Setup(g => g.EnforceGlobalIcmpBlock(It.IsAny<AppDatabase>()))
            .Callback(() => calls.Add("global"))
            .Throws(new InvalidOperationException("global icmp failed"));

        var result = CreateOrchestrator().ApplyAccountFirewallSettings(
            Sid,
            Username,
            previous,
            final,
            database,
            saveAction: () => calls.Add("save"));

        Assert.False(result.HasBlockingFailure);
        Assert.True(result.ConfigSaved);
        Assert.Equal(["account", "global", "save"], calls);
        Assert.Null(database.GetAccount(Sid));
        Assert.Contains(result.EnforcementEntries, entry =>
            entry.Layer == FirewallEnforcementLayer.GlobalIcmp
            && entry.Status == FirewallEnforcementStatus.RetryScheduled
            && entry.Error == "global icmp failed");
    }

    [Fact]
    public void ApplyAccountFirewallSettings_GlobalIcmpPhase_RunsAfterPendingDomainProcessing()
    {
        var database = Database(new FirewallAccountSettings());
        var calls = new List<string>();
        _accountRuleApplier
            .Setup(a => a.ApplyFirewallRules(
                Sid,
                Username,
                It.IsAny<FirewallAccountSettings>(),
                It.IsAny<FirewallAccountSettings?>(),
                It.IsAny<IReadOnlyDictionary<string, IReadOnlyList<string>>>()))
            .Callback(() => calls.Add("account"))
            .Returns(new FirewallAccountRuleApplyResult(
                true,
                [new FirewallPendingDomainResolution(Sid, "example.com")]));
        _pendingDomainProcessor
            .Setup(p => p.ProcessPendingDomains(It.IsAny<IReadOnlyList<FirewallPendingDomainResolution>>()))
            .Callback(() => calls.Add("pending"));
        _globalIcmpTrigger
            .Setup(g => g.EnforceGlobalIcmpBlock(It.IsAny<AppDatabase>()))
            .Callback(() => calls.Add("global"));

        CreateOrchestrator().ApplyAccountFirewallSettings(
            Sid,
            Username,
            new FirewallAccountSettings(),
            new FirewallAccountSettings { AllowInternet = false },
            database,
            saveAction: () => calls.Add("save"));

        Assert.Equal(["save", "account", "pending", "global"], calls);
    }

    [Fact]
    public void ApplyAccountFirewallSettings_RemoveOrLoosenFailureBeforeDelayedSave_ReportsConfigNotSaved()
    {
        var previous = new FirewallAccountSettings { AllowInternet = false };
        var final = new FirewallAccountSettings { AllowInternet = true };
        var database = Database(previous.Clone());
        var calls = new List<string>();

        _accountRuleApplier
            .Setup(a => a.ApplyFirewallRules(
                Sid,
                Username,
                It.IsAny<FirewallAccountSettings>(),
                It.IsAny<FirewallAccountSettings?>(),
                It.IsAny<IReadOnlyDictionary<string, IReadOnlyList<string>>>()))
            .Callback(() => calls.Add("apply"))
            .Returns(new FirewallAccountRuleApplyResult(
                false,
                [],
                FirewallEnforcementLayer.AccountRules,
                "account rules failed"));

        var result = CreateOrchestrator().ApplyAccountFirewallSettings(
            Sid,
            Username,
            previous,
            final,
            database,
            saveAction: () => calls.Add("save"));

        Assert.False(result.ConfigSaved);
        Assert.Equal(["apply"], calls);
        Assert.False(database.GetAccount(Sid)!.Firewall.AllowInternet);
    }

    [Fact]
    public void EnforceAll_FirstAccountFails_LaterAccountsStillRun_AndFailureIsReturned()
    {
        var database = new AppDatabase
        {
            Accounts =
            [
                new AccountEntry { Sid = Sid, Firewall = new FirewallAccountSettings { AllowInternet = false } },
                new AccountEntry { Sid = Sid2, Firewall = new FirewallAccountSettings { AllowInternet = false } }
            ],
            SidNames =
            {
                [Sid] = Username,
                [Sid2] = "bob"
            }
        };
        _accountRuleApplier
            .SetupSequence(a => a.ApplyFirewallRules(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<FirewallAccountSettings>(),
                It.IsAny<FirewallAccountSettings?>(),
                It.IsAny<IReadOnlyDictionary<string, IReadOnlyList<string>>>()))
            .Returns(new FirewallAccountRuleApplyResult(false, [], FirewallEnforcementLayer.WfpFilters, "first failed"))
            .Returns(new FirewallAccountRuleApplyResult(true, []));

        var result = CreateOrchestrator().EnforceAll(database);

        Assert.True(result.HasFailures);
        Assert.Contains(result.Failures, failure =>
            failure.Layer == FirewallEnforcementLayer.WfpFilters
            && failure.AccountSid == Sid
            && failure.Message == "first failed");
        Assert.Contains(_retryState.GetRetryEntries(), entry => entry.Layer == FirewallEnforcementLayer.AccountRules && entry.Key == Sid);
        Assert.Contains(_retryState.GetRetryEntries(), entry => entry.Layer == FirewallEnforcementLayer.WfpFilters && entry.Key == Sid);
        _refreshRequester.Verify(r => r.RequestRefresh(), Times.Once);
        _accountRuleApplier.Verify(a => a.ApplyFirewallRules(
            Sid2,
            "bob",
            It.IsAny<FirewallAccountSettings>(),
            It.IsAny<FirewallAccountSettings?>(),
            It.IsAny<IReadOnlyDictionary<string, IReadOnlyList<string>>>()), Times.Once);
    }

    [Fact]
    public void EnforceAll_GlobalIcmpFailure_IsReturnedWithoutStoppingCleanup()
    {
        var database = Database(new FirewallAccountSettings { AllowInternet = false });
        _accountRuleApplier
            .Setup(a => a.ApplyFirewallRules(
                Sid,
                Username,
                It.IsAny<FirewallAccountSettings>(),
                It.IsAny<FirewallAccountSettings?>(),
                It.IsAny<IReadOnlyDictionary<string, IReadOnlyList<string>>>()))
            .Returns(new FirewallAccountRuleApplyResult(true, []));
        _globalIcmpTrigger
            .Setup(g => g.EnforceGlobalIcmpBlock(It.IsAny<AppDatabase>()))
            .Throws(new InvalidOperationException("global icmp failed"));

        var result = CreateOrchestrator().EnforceAll(database);

        Assert.Contains(result.Failures, failure =>
            failure.Layer == FirewallEnforcementLayer.GlobalIcmp
            && failure.Message == "global icmp failed");
        Assert.True(_retryState.IsGlobalIcmpDirty());
        _cleanupService.Verify(c => c.CleanupOrphanedRules(
            It.Is<IReadOnlySet<string>>(sids => sids.Contains(Sid)),
            It.Is<IEnumerable<string>>(sids => sids.Contains(Sid))), Times.Once);
    }

    private FirewallEnforcementOrchestrator CreateOrchestrator()
        => new(
            _log.Object,
            _domainCache,
            _retryState,
            _accountRuleApplier.Object,
            _cleanupService.Object,
            _pendingDomainProcessor.Object,
            _globalIcmpTrigger.Object,
            new FirewallApplyPlanner(),
            new FirewallApplyRetryCoordinator(_retryState, _refreshRequester.Object),
            new FirewallApplyPhaseExecutor(_domainCache, _accountRuleApplier.Object, _globalIcmpTrigger.Object));

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
}
