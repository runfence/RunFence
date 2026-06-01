using RunFence.Acl;
using RunFence.Acl.UI;
using RunFence.Core.Models;
using Xunit;

namespace RunFence.Tests;

public class AclApplyPostProcessingPolicyTests
{
    private static readonly AclApplyPhaseCatalog PhaseCatalog = new();

    [Fact]
    public void CleanupCompletedPending_RemovesCompletedGrantAndTraverseEntries()
    {
        var policy = new AclApplyPostProcessingPolicy(PhaseCatalog);
        var pending = new AclManagerPendingChanges();
        var grantEntry = new GrantedPathEntry { Path = @"C:\Grant", IsDeny = false };
        var traverseEntry = new GrantedPathEntry { Path = @"C:\Traverse", IsTraverseOnly = true };
        pending.Grants.AddGrant(grantEntry);
        pending.Traverse.AddTraverse(traverseEntry);
        var plan = new AclApplyPlanBuilder().Build(pending);
        var result = new AclApplyExecutionResult();
        result.MarkCompleted(AclPendingOperationKind.GrantAdd, grantEntry.Path, grantEntry.IsDeny);
        result.MarkCompleted(AclPendingOperationKind.TraverseAdd, traverseEntry.Path, null);

        policy.CleanupCompletedPending(plan, result, pending);

        Assert.False(pending.Grants.IsPendingAdd(grantEntry.Path, grantEntry.IsDeny));
        Assert.False(pending.Traverse.IsPendingTraverseAdd(traverseEntry.Path));
    }

    [Fact]
    public void GrantConfigMoveHasCompletedOperation_DetectsModificationViaOldAndNewModeKeys()
    {
        var policy = new AclApplyPostProcessingPolicy(PhaseCatalog);
        var entry = new GrantedPathEntry
        {
            Path = @"C:\Switch",
            IsDeny = false,
            SavedRights = SavedRightsState.DefaultForMode(false)
        };
        var modification = new PendingModification(
            entry,
            WasIsDeny: false,
            WasOwn: false,
            NewIsDeny: true,
            NewRights: SavedRightsState.DefaultForMode(true),
            WasRights: entry.SavedRights);
        var plan = new AclApplyPlan(
            PendingAdds: [],
            PendingRemoves: [],
            PendingModifications: [modification],
            PendingGrantFixes: [],
            PendingTraverseAdds: [],
            PendingTraverseRemoves: [],
            PendingTraverseFixes: [],
            PendingUntrackGrants: [],
            PendingUntrackTraverse: [],
            PendingConfigMoves: [],
            PendingTraverseConfigMoves: []);
        var result = new AclApplyExecutionResult();
        result.MarkCompleted(AclPendingOperationKind.GrantModification, entry.Path, entry.IsDeny);
        var denyEntry = entry.Clone();
        denyEntry.IsDeny = true;

        Assert.True(policy.GrantConfigMoveHasCompletedOperation(new PendingConfigMove(entry, @"C:\Configs\extra.rfn"), plan, result));
        Assert.True(policy.GrantConfigMoveHasCompletedOperation(new PendingConfigMove(denyEntry, @"C:\Configs\extra.rfn"), plan, result));
    }

    [Fact]
    public void ShouldKeepGrantConfigMove_ReturnsTrueForGrantFixError()
    {
        var policy = new AclApplyPostProcessingPolicy(PhaseCatalog);
        var entry = new GrantedPathEntry { Path = @"C:\GrantFix", IsDeny = false };
        var plan = new AclApplyPlan(
            PendingAdds: [],
            PendingRemoves: [],
            PendingModifications: [],
            PendingGrantFixes: [entry],
            PendingTraverseAdds: [],
            PendingTraverseRemoves: [],
            PendingTraverseFixes: [],
            PendingUntrackGrants: [],
            PendingUntrackTraverse: [],
            PendingConfigMoves: [],
            PendingTraverseConfigMoves: []);
        var result = new AclApplyExecutionResult();
        result.Errors.Add(new AclApplyError(
            AclPendingOperationKind.GrantFix,
            entry.Path,
            entry.IsDeny,
            new GrantOperationException(
                GrantApplyFailureStep.FixGrantAclApply,
                entry.Path,
                null,
                new InvalidOperationException("fix failed"))));

        Assert.True(policy.ShouldKeepGrantConfigMove(
            new PendingConfigMove(entry, @"C:\Configs\extra.rfn"),
            plan,
            result,
            new HashSet<(string Path, bool IsDeny)>(new GrantPathKeyComparer())));
    }

    [Fact]
    public void ShouldKeepTraverseConfigMove_ReturnsFalseWhenNoFailureTracked()
    {
        var policy = new AclApplyPostProcessingPolicy(PhaseCatalog);
        var entry = new GrantedPathEntry { Path = @"C:\Traverse", IsTraverseOnly = true };

        var keep = policy.ShouldKeepTraverseConfigMove(
            new PendingConfigMove(entry, @"C:\Configs\extra.rfn"),
            new AclApplyExecutionResult(),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        Assert.False(keep);
    }
}
