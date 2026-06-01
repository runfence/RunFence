using RunFence.Acl.UI;
using RunFence.Core.Models;
using Xunit;

namespace RunFence.Tests;

public class AclApplyPlanBuilderTests
{
    private static readonly AclApplyPhaseCatalog PhaseCatalog = new();

    [Fact]
    public void Build_CreatesStableSnapshot_AndCountsOperations()
    {
        var pending = new AclManagerPendingChanges();
        pending.Grants.AddGrant(new GrantedPathEntry { Path = @"C:\Add", IsDeny = false });
        pending.Grants.MarkGrantForRemoval(new GrantedPathEntry { Path = @"C:\Remove", IsDeny = false });
        var modifiedEntry = new GrantedPathEntry { Path = @"C:\Modify", IsDeny = false, SavedRights = SavedRightsState.DefaultForMode(false) };
        pending.Grants.ModifyGrant(modifiedEntry, new PendingModification(
            modifiedEntry,
            WasIsDeny: false,
            WasOwn: false,
            NewIsDeny: false,
            NewRights: SavedRightsState.DefaultForMode(false)));
        pending.Grants.AddGrantFix(new GrantedPathEntry { Path = @"C:\GrantFix", IsDeny = false });
        pending.Traverse.AddTraverse(new GrantedPathEntry { Path = @"C:\TraverseAdd", IsTraverseOnly = true });
        pending.Traverse.MarkTraverseForRemoval(new GrantedPathEntry { Path = @"C:\TraverseRemove", IsTraverseOnly = true });
        pending.Traverse.AddTraverseFix(new GrantedPathEntry { Path = @"C:\TraverseFix", IsTraverseOnly = true });
        pending.Grants.UntrackGrant(new GrantedPathEntry { Path = @"C:\UntrackGrant", IsDeny = false });
        pending.Traverse.UntrackTraverse(new GrantedPathEntry { Path = @"C:\UntrackTraverse", IsTraverseOnly = true });
        pending.Grants.MoveGrantConfig(new GrantedPathEntry { Path = @"C:\Move", IsDeny = false }, "extra.rfn");

        var builder = new AclApplyPlanBuilder();
        var plan = builder.Build(pending);

        pending.Clear();

        Assert.Equal(9, PhaseCatalog.GetTotalOperations(plan));
        Assert.True(PhaseCatalog.HasWork(plan));
        Assert.Single(plan.PendingAdds);
        Assert.Single(plan.PendingGrantFixes);
        Assert.Single(plan.PendingConfigMoves);
    }

    [Fact]
    public void Build_ConfigOnlyWork_HasWorkTrue_WithZeroOperations()
    {
        var pending = new AclManagerPendingChanges();
        pending.Grants.MoveGrantConfig(new GrantedPathEntry { Path = @"C:\MoveOnly", IsDeny = false }, "extra.rfn");

        var plan = new AclApplyPlanBuilder().Build(pending);

        Assert.Equal(0, PhaseCatalog.GetTotalOperations(plan));
        Assert.True(PhaseCatalog.HasWork(plan));
    }
}
