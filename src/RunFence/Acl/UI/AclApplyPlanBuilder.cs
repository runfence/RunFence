namespace RunFence.Acl.UI;

public class AclApplyPlanBuilder
{
    public AclApplyPlan Build(AclManagerPendingChanges pending)
    {
        var grantChanges = pending.Grants.GetSnapshot();
        var traverseChanges = pending.Traverse.GetSnapshot();

        return new AclApplyPlan(
            PendingAdds: grantChanges.PendingAdds.Values.ToList(),
            PendingRemoves: grantChanges.PendingRemoves.Values.ToList(),
            PendingModifications: grantChanges.PendingModifications.Values.ToList(),
            PendingGrantFixes: grantChanges.PendingGrantFixes.Values.ToList(),
            PendingTraverseAdds: traverseChanges.PendingAdds.Values.ToList(),
            PendingTraverseRemoves: traverseChanges.PendingRemoves.Values.ToList(),
            PendingTraverseFixes: traverseChanges.PendingFixes.Values.ToList(),
            PendingUntrackGrants: grantChanges.PendingUntrack.Values.ToList(),
            PendingUntrackTraverse: traverseChanges.PendingUntrack.Values.ToList(),
            PendingConfigMoves: grantChanges.PendingConfigMoves.Values.ToList(),
            PendingTraverseConfigMoves: traverseChanges.PendingConfigMoves.Values.ToList());
    }
}
