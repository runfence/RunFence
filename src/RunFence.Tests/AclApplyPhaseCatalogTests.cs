using RunFence.Acl.UI;
using RunFence.Core.Models;
using Xunit;

namespace RunFence.Tests;

public class AclApplyPhaseCatalogTests
{
    private readonly AclApplyPhaseCatalog _catalog = new();

    [Fact]
    public void OrderedPhases_ReturnsExpectedPhaseOrder()
    {
        Assert.Equal(
        [
            AclApplyPhase.GrantRemove,
            AclApplyPhase.TraverseRemove,
            AclApplyPhase.GrantUntrack,
            AclApplyPhase.TraverseUntrack,
            AclApplyPhase.GrantAdd,
            AclApplyPhase.GrantModification,
            AclApplyPhase.TraverseAdd,
            AclApplyPhase.GrantFix,
            AclApplyPhase.TraverseFix
        ], _catalog.OrderedPhases.Select(phase => phase.Phase));
    }

    [Fact]
    public void GetTotalOperations_SumsAllOperationPhases()
    {
        var plan = new AclApplyPlan(
            PendingAdds: [new GrantedPathEntry { Path = @"C:\Add", IsDeny = false }],
            PendingRemoves: [new GrantedPathEntry { Path = @"C:\Remove", IsDeny = false }],
            PendingModifications: [new PendingModification(new GrantedPathEntry { Path = @"C:\Modify", IsDeny = false }, false, false, false, null)],
            PendingGrantFixes: [new GrantedPathEntry { Path = @"C:\GrantFix", IsDeny = false }],
            PendingTraverseAdds: [new GrantedPathEntry { Path = @"C:\TraverseAdd", IsTraverseOnly = true }],
            PendingTraverseRemoves: [new GrantedPathEntry { Path = @"C:\TraverseRemove", IsTraverseOnly = true }],
            PendingTraverseFixes: [new GrantedPathEntry { Path = @"C:\TraverseFix", IsTraverseOnly = true }],
            PendingUntrackGrants: [new GrantedPathEntry { Path = @"C:\GrantUntrack", IsDeny = false }],
            PendingUntrackTraverse: [new GrantedPathEntry { Path = @"C:\TraverseUntrack", IsTraverseOnly = true }],
            PendingConfigMoves: [],
            PendingTraverseConfigMoves: []);

        Assert.Equal(9, _catalog.GetTotalOperations(plan));
        Assert.Equal(1, _catalog.GetPhaseCount(AclApplyPhase.GrantModification, plan));
    }

    [Fact]
    public void HasWork_WithOnlyOperations_ReturnsTrue()
    {
        var plan = new AclApplyPlan(
            PendingAdds: [new GrantedPathEntry { Path = @"C:\Add", IsDeny = false }],
            PendingRemoves: [],
            PendingModifications: [],
            PendingGrantFixes: [],
            PendingTraverseAdds: [],
            PendingTraverseRemoves: [],
            PendingTraverseFixes: [],
            PendingUntrackGrants: [],
            PendingUntrackTraverse: [],
            PendingConfigMoves: [],
            PendingTraverseConfigMoves: []);

        Assert.True(_catalog.HasWork(plan));
    }

    [Fact]
    public void HasWork_WithOnlyConfigMoves_ReturnsTrue()
    {
        var plan = new AclApplyPlan(
            PendingAdds: [],
            PendingRemoves: [],
            PendingModifications: [],
            PendingGrantFixes: [],
            PendingTraverseAdds: [],
            PendingTraverseRemoves: [],
            PendingTraverseFixes: [],
            PendingUntrackGrants: [],
            PendingUntrackTraverse: [],
            PendingConfigMoves: [new PendingConfigMove(new GrantedPathEntry { Path = @"C:\Move", IsDeny = false }, @"C:\Configs\extra.rfn")],
            PendingTraverseConfigMoves: []);

        Assert.True(_catalog.HasWork(plan));
        Assert.Equal(0, _catalog.GetTotalOperations(plan));
    }
}
