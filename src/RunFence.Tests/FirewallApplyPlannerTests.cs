using RunFence.Core.Models;
using RunFence.Firewall;
using Xunit;

namespace RunFence.Tests;

public class FirewallApplyPlannerTests
{
    private const string Sid = "S-1-5-21-1000-1000-1000-1001";
    private readonly FirewallApplyPlanner _planner = new();

    [Fact]
    public void BuildApplyPlan_AddOrTighten_CreatesPersistBeforePhase()
    {
        var previous = new FirewallAccountSettings { AllowLocalhost = true };
        var requested = new FirewallAccountSettings { AllowLocalhost = false };

        var plan = _planner.BuildApplyPlan(Sid, previous, requested);

        var phase = Assert.Single(plan.Phases);
        Assert.Equal(FirewallApplyPlanPhaseKind.AddOrTighten, phase.Kind);
        Assert.True(phase.PersistConfigBeforeExecution);
        Assert.True(phase.ShouldPersist);
        Assert.NotNull(phase.AccountOperation);
        Assert.Null(phase.GlobalIcmpOperation);
        Assert.False(phase.TargetSettings.AllowLocalhost);
        Assert.True(plan.UpdatesAccountRetryState);
        Assert.False(plan.UpdatesGlobalIcmpRetryState);
        Assert.False(plan.RequiresNoOpSuccessEntries);
    }

    [Fact]
    public void BuildApplyPlan_RemoveOrLoosen_CreatesPersistAfterPhase()
    {
        var previous = new FirewallAccountSettings { AllowLocalhost = false };
        var requested = new FirewallAccountSettings { AllowLocalhost = true };

        var plan = _planner.BuildApplyPlan(Sid, previous, requested);

        var phase = Assert.Single(plan.Phases);
        Assert.Equal(FirewallApplyPlanPhaseKind.RemoveOrLoosen, phase.Kind);
        Assert.False(phase.PersistConfigBeforeExecution);
        Assert.True(phase.ShouldPersist);
        Assert.NotNull(phase.AccountOperation);
        Assert.Null(phase.GlobalIcmpOperation);
        Assert.True(phase.TargetSettings.AllowLocalhost);
    }

    [Fact]
    public void BuildApplyPlan_NoOp_ReturnsNoOpMetadata()
    {
        var settings = new FirewallAccountSettings
        {
            AllowInternet = false,
            Allowlist =
            [
                new FirewallAllowlistEntry
                {
                    Value = "example.com",
                    IsDomain = true
                }
            ],
            LocalhostPortExemptions = ["53", "8080"]
        };

        var plan = _planner.BuildApplyPlan(Sid, settings, settings.Clone());

        Assert.Empty(plan.Phases);
        Assert.True(plan.RequiresNoOpSuccessEntries);
        Assert.False(plan.UpdatesAccountRetryState);
        Assert.False(plan.UpdatesGlobalIcmpRetryState);
    }

    [Fact]
    public void BuildApplyPlan_NormalizationPreservesEquivalentAllowlistAndPortEntries()
    {
        var previousAllowlistEntry = new FirewallAllowlistEntry
        {
            Value = " Example.com ",
            IsDomain = true
        };
        var previous = new FirewallAccountSettings
        {
            AllowInternet = false,
            Allowlist = [previousAllowlistEntry],
            LocalhostPortExemptions = ["53", " 8080 "]
        };
        var requested = new FirewallAccountSettings
        {
            AllowInternet = false,
            AllowLocalhost = false,
            Allowlist =
            [
                new FirewallAllowlistEntry
                {
                    Value = "example.com",
                    IsDomain = true
                }
            ],
            LocalhostPortExemptions = ["53", "8080"]
        };

        var plan = _planner.BuildApplyPlan(Sid, previous, requested);

        var phase = Assert.Single(plan.Phases);
        Assert.NotNull(phase.AccountOperation);
        Assert.Null(phase.GlobalIcmpOperation);
        var allowlistEntry = Assert.Single(plan.Phases[0].TargetSettings.Allowlist);
        Assert.NotSame(previousAllowlistEntry, allowlistEntry);
        Assert.Equal(" Example.com ", allowlistEntry.Value);
        Assert.Equal(["53", " 8080 "], plan.Phases[0].TargetSettings.LocalhostPortExemptions);
    }

    [Fact]
    public void BuildApplyPlan_AllowlistChange_AddsGlobalIcmpOperation()
    {
        var previous = new FirewallAccountSettings { AllowInternet = false };
        var requested = new FirewallAccountSettings
        {
            AllowInternet = false,
            Allowlist =
            [
                new FirewallAllowlistEntry
                {
                    Value = "example.com",
                    IsDomain = true
                }
            ]
        };

        var plan = _planner.BuildApplyPlan(Sid, previous, requested);

        Assert.Equal(2, plan.Phases.Count);
        var accountPhase = Assert.Single(plan.Phases, phase => phase.AccountOperation is not null);
        var globalPhase = Assert.Single(plan.Phases, phase => phase.GlobalIcmpOperation is not null);
        Assert.False(accountPhase.ShouldPersist);
        Assert.True(globalPhase.ShouldPersist);
        Assert.True(plan.UpdatesGlobalIcmpRetryState);
    }
}
