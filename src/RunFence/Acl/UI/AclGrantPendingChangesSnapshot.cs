using RunFence.Core.Models;

namespace RunFence.Acl.UI;

public sealed record AclGrantPendingChangesSnapshot(
    IReadOnlyDictionary<(string Path, bool IsDeny), GrantedPathEntry> PendingAdds,
    IReadOnlyDictionary<(string Path, bool IsDeny), GrantedPathEntry> PendingRemoves,
    IReadOnlyDictionary<(string Path, bool IsDeny), PendingModification> PendingModifications,
    IReadOnlyDictionary<(string Path, bool IsDeny), GrantedPathEntry> PendingGrantFixes,
    IReadOnlyDictionary<(string Path, bool IsDeny), GrantedPathEntry> PendingUntrack,
    IReadOnlyDictionary<(string Path, bool IsDeny), PendingConfigMove> PendingConfigMoves);
