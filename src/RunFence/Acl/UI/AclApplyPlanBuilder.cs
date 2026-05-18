namespace RunFence.Acl.UI;

public class AclApplyPlanBuilder
{
    public AclApplyPlan Build(AclManagerPendingChanges pending)
    {
        return new AclApplyPlan(
            PendingAdds: pending.PendingAdds.Values.ToList(),
            PendingRemoves: pending.PendingRemoves.Values.ToList(),
            PendingModifications: pending.PendingModifications.Values.ToList(),
            PendingGrantFixes: pending.PendingGrantFixes.Values.ToList(),
            PendingTraverseAdds: pending.PendingTraverseAdds.Values.ToList(),
            PendingTraverseRemoves: pending.PendingTraverseRemoves.Values.ToList(),
            PendingTraverseFixes: pending.PendingTraverseFixes.Values.ToList(),
            PendingUntrackGrants: pending.PendingUntrackGrants.Values.ToList(),
            PendingUntrackTraverse: pending.PendingUntrackTraverse.Values.ToList(),
            PendingConfigMoves: pending.PendingConfigMoves.Values.ToList(),
            PendingTraverseConfigMoves: pending.PendingTraverseConfigMoves.Values.ToList());
    }
}
