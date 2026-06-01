using RunFence.Core.Models;

namespace RunFence.Acl.UI;

public sealed record AclApplyPlan(
    IReadOnlyList<GrantedPathEntry> PendingAdds,
    IReadOnlyList<GrantedPathEntry> PendingRemoves,
    IReadOnlyList<PendingModification> PendingModifications,
    IReadOnlyList<GrantedPathEntry> PendingGrantFixes,
    IReadOnlyList<GrantedPathEntry> PendingTraverseAdds,
    IReadOnlyList<GrantedPathEntry> PendingTraverseRemoves,
    IReadOnlyList<GrantedPathEntry> PendingTraverseFixes,
    IReadOnlyList<GrantedPathEntry> PendingUntrackGrants,
    IReadOnlyList<GrantedPathEntry> PendingUntrackTraverse,
    IReadOnlyList<PendingConfigMove> PendingConfigMoves,
    IReadOnlyList<PendingConfigMove> PendingTraverseConfigMoves);
