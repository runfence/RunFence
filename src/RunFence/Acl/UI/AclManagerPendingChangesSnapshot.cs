using RunFence.Core.Models;

namespace RunFence.Acl.UI;

public sealed record AclManagerPendingChangesSnapshot(
    IReadOnlyDictionary<(string Path, bool IsDeny), GrantedPathEntry> PendingAdds,
    IReadOnlyDictionary<(string Path, bool IsDeny), GrantedPathEntry> PendingRemoves,
    IReadOnlyDictionary<(string Path, bool IsDeny), PendingModification> PendingModifications,
    IReadOnlyDictionary<(string Path, bool IsDeny), GrantedPathEntry> PendingGrantFixes,
    IReadOnlyDictionary<string, GrantedPathEntry> PendingTraverseAdds,
    IReadOnlyDictionary<string, GrantedPathEntry> PendingTraverseRemoves,
    IReadOnlyDictionary<string, GrantedPathEntry> PendingTraverseFixes,
    IReadOnlyDictionary<(string Path, bool IsDeny), GrantedPathEntry> PendingUntrackGrants,
    IReadOnlyDictionary<string, GrantedPathEntry> PendingUntrackTraverse,
    IReadOnlyDictionary<(string Path, bool IsDeny), PendingConfigMove> PendingConfigMoves,
    IReadOnlyDictionary<string, PendingConfigMove> PendingTraverseConfigMoves);
