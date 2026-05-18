using RunFence.Core.Models;

namespace RunFence.Acl;

public readonly record struct GrantConflictResult(
    GrantedPathEntry? SameModeEntry,
    GrantedPathEntry? OppositeModeEntry)
{
    public bool HasSameModeEntry => SameModeEntry != null;

    public bool HasOppositeModeEntry => OppositeModeEntry != null;
}
