using Moq;
using RunFence.Core.Models;
using RunFence.Firewall;
using RunFence.Firewall.Wfp;
using Xunit;

namespace RunFence.Tests;

public class FirewallApplyPhaseExecutorTests
{
    private const string Sid = "S-1-5-21-1000-1000-1000-1001";
    private const string Username = "alice";

    private readonly FirewallResolvedDomainCache _domainCache = new(new FirewallDomainDirtyTracker());
    private readonly Mock<IFirewallAccountRuleApplier> _accountRuleApplier = new();
    private readonly Mock<IGlobalIcmpEnforcementTrigger> _globalIcmpTrigger = new();

    [Fact]
    public void ExecutePhase_AddOrTighten_PersistsBeforeAccountAndGlobalWork()
    {
        var previous = new FirewallAccountSettings { AllowInternet = true };
        var requested = new FirewallAccountSettings { AllowInternet = false };
        var phase = CreatePhase(FirewallApplyPlanPhaseKind.AddOrTighten, persistBeforeExecution: true, previous, requested);
        var database = Database(previous.Clone());
        var calls = new List<string>();

        _accountRuleApplier
            .Setup(a => a.ApplyFirewallRules(
                Sid,
                Username,
                It.IsAny<FirewallAccountSettings>(),
                It.IsAny<FirewallAccountSettings?>(),
                It.IsAny<IReadOnlyDictionary<string, IReadOnlyList<string>>>()))
            .Callback(() =>
            {
                calls.Add("account");
                Assert.False(database.GetAccount(Sid)!.Firewall.AllowInternet);
            })
            .Returns(new FirewallAccountRuleApplyResult(true, []));
        _globalIcmpTrigger
            .Setup(g => g.EnforceGlobalIcmpBlock(It.IsAny<AppDatabase>()))
            .Callback(() => calls.Add("global"));

        var result = CreateExecutor().ExecutePhase(
            phase,
            () => calls.Add("save"),
            database,
            Sid,
            Username,
            CancellationToken.None);

        Assert.True(result.CanContinue);
        Assert.True(result.PersistenceCompleted);
        Assert.NotNull(result.AccountApplyResult);
        Assert.True(result.AccountApplyResult.Succeeded);
        Assert.True(result.GlobalIcmpApplied);
        Assert.Null(result.GlobalIcmpError);
        Assert.Empty(result.Entries);
        Assert.Equal(["save", "account", "global"], calls);
        Assert.False(database.GetAccount(Sid)!.Firewall.AllowInternet);
    }

    [Fact]
    public void ExecutePhase_RemoveOrLoosen_PersistsAfterAccountAndGlobalWork()
    {
        var previous = new FirewallAccountSettings { AllowInternet = false };
        var requested = new FirewallAccountSettings { AllowInternet = true };
        var phase = CreatePhase(FirewallApplyPlanPhaseKind.RemoveOrLoosen, persistBeforeExecution: false, previous, requested);
        var database = Database(previous.Clone());
        var calls = new List<string>();

        _accountRuleApplier
            .Setup(a => a.ApplyFirewallRules(
                Sid,
                Username,
                It.IsAny<FirewallAccountSettings>(),
                It.IsAny<FirewallAccountSettings?>(),
                It.IsAny<IReadOnlyDictionary<string, IReadOnlyList<string>>>()))
            .Callback(() =>
            {
                calls.Add("account");
                Assert.False(database.GetAccount(Sid)!.Firewall.AllowInternet);
            })
            .Returns(new FirewallAccountRuleApplyResult(true, []));
        _globalIcmpTrigger
            .Setup(g => g.EnforceGlobalIcmpBlock(It.IsAny<AppDatabase>()))
            .Callback(() => calls.Add("global"));

        var result = CreateExecutor().ExecutePhase(
            phase,
            () => calls.Add("save"),
            database,
            Sid,
            Username,
            CancellationToken.None);

        Assert.True(result.CanContinue);
        Assert.True(result.PersistenceCompleted);
        Assert.NotNull(result.AccountApplyResult);
        Assert.True(result.AccountApplyResult.Succeeded);
        Assert.True(result.GlobalIcmpApplied);
        Assert.Null(result.GlobalIcmpError);
        Assert.Empty(result.Entries);
        Assert.Equal(["account", "global", "save"], calls);
        Assert.Null(database.GetAccount(Sid));
    }

    [Fact]
    public void ExecutePhase_AccountRuleException_ConvertsFailureAndStopsPhase()
    {
        var previous = new FirewallAccountSettings { AllowInternet = false };
        var requested = new FirewallAccountSettings { AllowInternet = true };
        var phase = CreatePhase(FirewallApplyPlanPhaseKind.RemoveOrLoosen, persistBeforeExecution: false, previous, requested);
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
            .Throws(new WfpFilterHelperException("wfp failed"));

        var result = CreateExecutor().ExecutePhase(
            phase,
            () => calls.Add("save"),
            database,
            Sid,
            Username,
            CancellationToken.None);

        Assert.False(result.CanContinue);
        Assert.False(result.PersistenceCompleted);
        var accountResult = Assert.IsType<FirewallAccountRuleApplyResult>(result.AccountApplyResult);
        Assert.False(accountResult.Succeeded);
        Assert.Equal(FirewallEnforcementLayer.WfpFilters, accountResult.FailedLayer);
        Assert.Equal("wfp failed", accountResult.ErrorMessage);
        Assert.False(result.GlobalIcmpApplied);
        Assert.Null(result.GlobalIcmpError);
        var entry = Assert.Single(result.Entries);
        Assert.Equal(FirewallEnforcementLayer.WfpFilters, entry.Layer);
        Assert.Equal(FirewallEnforcementStatus.Failed, entry.Status);
        Assert.Equal("wfp failed", entry.Error);
        Assert.Equal(["account"], calls);
        Assert.False(database.GetAccount(Sid)!.Firewall.AllowInternet);
    }

    [Fact]
    public async Task ExecutePhaseAsync_RemoveOrLoosenGlobalIcmpFailure_AddsRetryEntryAndContinuesToDelayedPersist()
    {
        var previous = new FirewallAccountSettings { AllowInternet = false };
        var requested = new FirewallAccountSettings { AllowInternet = true };
        var phase = CreatePhase(FirewallApplyPlanPhaseKind.RemoveOrLoosen, persistBeforeExecution: false, previous, requested);
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
            .Setup(g => g.EnforceGlobalIcmpBlockAsync(It.IsAny<GlobalIcmpPolicyInput>(), It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("global"))
            .ThrowsAsync(new InvalidOperationException("global failed"));

        var result = await CreateExecutor().ExecutePhaseAsync(
            phase,
            _ =>
            {
                calls.Add("save");
                return Task.CompletedTask;
            },
            database,
            Sid,
            Username,
            CancellationToken.None);

        Assert.True(result.CanContinue);
        Assert.True(result.PersistenceCompleted);
        Assert.NotNull(result.AccountApplyResult);
        Assert.True(result.AccountApplyResult.Succeeded);
        Assert.False(result.GlobalIcmpApplied);
        Assert.Equal("global failed", result.GlobalIcmpError);
        Assert.Equal(["account", "global", "save"], calls);
        var entry = Assert.Single(result.Entries);
        Assert.Equal(FirewallEnforcementLayer.GlobalIcmp, entry.Layer);
        Assert.Equal(FirewallEnforcementStatus.RetryScheduled, entry.Status);
        Assert.Equal("global failed", entry.Error);
        Assert.Null(database.GetAccount(Sid));
    }

    [Fact]
    public void ExecutePhase_AddOrTightenGlobalIcmpFailure_AddsRetryEntryAndStopsPhase()
    {
        var previous = new FirewallAccountSettings { AllowInternet = true };
        var requested = new FirewallAccountSettings { AllowInternet = false };
        var phase = CreatePhase(FirewallApplyPlanPhaseKind.AddOrTighten, persistBeforeExecution: true, previous, requested);
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
            .Throws(new InvalidOperationException("global failed"));

        var result = CreateExecutor().ExecutePhase(
            phase,
            () => calls.Add("save"),
            database,
            Sid,
            Username,
            CancellationToken.None);

        Assert.False(result.CanContinue);
        Assert.True(result.PersistenceCompleted);
        Assert.NotNull(result.AccountApplyResult);
        Assert.True(result.AccountApplyResult.Succeeded);
        Assert.False(result.GlobalIcmpApplied);
        Assert.Equal("global failed", result.GlobalIcmpError);
        Assert.Equal(["save", "account", "global"], calls);
        var entry = Assert.Single(result.Entries);
        Assert.Equal(FirewallEnforcementLayer.GlobalIcmp, entry.Layer);
        Assert.Equal(FirewallEnforcementStatus.RetryScheduled, entry.Status);
        Assert.Equal("global failed", entry.Error);
        Assert.False(database.GetAccount(Sid)!.Firewall.AllowInternet);
    }

    [Fact]
    public void ExecutePhase_RepeatedExecutionOnSamePhase_DoesNotCarryStateBetweenResults()
    {
        var previous = new FirewallAccountSettings { AllowInternet = true };
        var requested = new FirewallAccountSettings { AllowInternet = false };
        var phase = CreatePhase(FirewallApplyPlanPhaseKind.AddOrTighten, persistBeforeExecution: true, previous, requested);
        var firstDatabase = Database(previous.Clone());
        var secondDatabase = Database(previous.Clone());

        _accountRuleApplier
            .Setup(a => a.ApplyFirewallRules(
                Sid,
                Username,
                It.IsAny<FirewallAccountSettings>(),
                It.IsAny<FirewallAccountSettings?>(),
                It.IsAny<IReadOnlyDictionary<string, IReadOnlyList<string>>>()))
            .Returns(new FirewallAccountRuleApplyResult(true, []));
        _globalIcmpTrigger
            .SetupSequence(g => g.EnforceGlobalIcmpBlock(It.IsAny<AppDatabase>()))
            .Throws(new InvalidOperationException("global failed"))
            .Pass();

        var executor = CreateExecutor();
        var firstResult = executor.ExecutePhase(
            phase,
            () => { },
            firstDatabase,
            Sid,
            Username,
            CancellationToken.None);
        var secondResult = executor.ExecutePhase(
            phase,
            () => { },
            secondDatabase,
            Sid,
            Username,
            CancellationToken.None);

        Assert.False(firstResult.CanContinue);
        Assert.Equal("global failed", firstResult.GlobalIcmpError);
        Assert.Single(firstResult.Entries);

        Assert.True(secondResult.CanContinue);
        Assert.True(secondResult.GlobalIcmpApplied);
        Assert.Null(secondResult.GlobalIcmpError);
        Assert.Empty(secondResult.Entries);
    }

    private FirewallApplyPhaseExecutor CreateExecutor()
        => new(_domainCache, _accountRuleApplier.Object, _globalIcmpTrigger.Object);

    private static FirewallApplyPlanPhase CreatePhase(
        FirewallApplyPlanPhaseKind kind,
        bool persistBeforeExecution,
        FirewallAccountSettings previous,
        FirewallAccountSettings requested)
    {
        var accountOperation = new FirewallOperation(
            FirewallEnforcementLayer.AccountRules,
            Sid,
            previous.Clone(),
            requested.Clone());
        var globalOperation = new FirewallOperation(
            FirewallEnforcementLayer.GlobalIcmp,
            Sid,
            previous.Clone(),
            requested.Clone());
        return new FirewallApplyPlanPhase(
            kind,
            persistBeforeExecution,
            shouldPersist: true,
            requested.Clone(),
            accountOperation,
            globalOperation);
    }

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
