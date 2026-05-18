using RunFence.Acl.UI;
using RunFence.Core.Models;
using Xunit;

namespace RunFence.Tests;

public class AclApplyPlanBuilderTests
{
    [Fact]
    public void Build_CreatesStableSnapshot_AndCountsOperations()
    {
        var pending = new AclManagerPendingChanges();
        pending.PendingAdds[(@"C:\Add", false)] = new GrantedPathEntry { Path = @"C:\Add", IsDeny = false };
        pending.PendingRemoves[(@"C:\Remove", false)] = new GrantedPathEntry { Path = @"C:\Remove", IsDeny = false };
        pending.PendingModifications[(@"C:\Modify", false)] = new PendingModification(
            new GrantedPathEntry { Path = @"C:\Modify", IsDeny = false, SavedRights = SavedRightsState.DefaultForMode(false) },
            WasIsDeny: false,
            WasOwn: false,
            NewIsDeny: false,
            NewRights: SavedRightsState.DefaultForMode(false));
        pending.PendingGrantFixes[(@"C:\GrantFix", false)] = new GrantedPathEntry { Path = @"C:\GrantFix", IsDeny = false };
        pending.PendingTraverseAdds[@"C:\TraverseAdd"] = new GrantedPathEntry { Path = @"C:\TraverseAdd", IsTraverseOnly = true };
        pending.PendingTraverseRemoves[@"C:\TraverseRemove"] = new GrantedPathEntry { Path = @"C:\TraverseRemove", IsTraverseOnly = true };
        pending.PendingTraverseFixes[@"C:\TraverseFix"] = new GrantedPathEntry { Path = @"C:\TraverseFix", IsTraverseOnly = true };
        pending.PendingUntrackGrants[(@"C:\UntrackGrant", false)] = new GrantedPathEntry { Path = @"C:\UntrackGrant", IsDeny = false };
        pending.PendingUntrackTraverse[@"C:\UntrackTraverse"] = new GrantedPathEntry { Path = @"C:\UntrackTraverse", IsTraverseOnly = true };
        pending.PendingConfigMoves[(@"C:\Move", false)] =
            new PendingConfigMove(new GrantedPathEntry { Path = @"C:\Move", IsDeny = false }, "extra.rfn");

        var builder = new AclApplyPlanBuilder();
        var plan = builder.Build(pending);

        pending.PendingAdds.Clear();

        Assert.Equal(9, plan.TotalOperations);
        Assert.True(plan.HasWork);
        Assert.Single(plan.PendingAdds);
        Assert.Single(plan.PendingGrantFixes);
        Assert.Single(plan.PendingConfigMoves);
    }

    [Fact]
    public void Build_ConfigOnlyWork_HasWorkTrue_WithZeroOperations()
    {
        var pending = new AclManagerPendingChanges();
        pending.PendingConfigMoves[(@"C:\MoveOnly", false)] =
            new PendingConfigMove(new GrantedPathEntry { Path = @"C:\MoveOnly", IsDeny = false }, "extra.rfn");

        var plan = new AclApplyPlanBuilder().Build(pending);

        Assert.Equal(0, plan.TotalOperations);
        Assert.True(plan.HasWork);
    }
}
