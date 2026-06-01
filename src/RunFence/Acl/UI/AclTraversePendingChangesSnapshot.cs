using RunFence.Core.Models;

namespace RunFence.Acl.UI;

public sealed record AclTraversePendingChangesSnapshot(
    IReadOnlyDictionary<string, GrantedPathEntry> PendingAdds,
    IReadOnlyDictionary<string, GrantedPathEntry> PendingRemoves,
    IReadOnlyDictionary<string, GrantedPathEntry> PendingFixes,
    IReadOnlyDictionary<string, GrantedPathEntry> PendingUntrack,
    IReadOnlyDictionary<string, PendingConfigMove> PendingConfigMoves);
