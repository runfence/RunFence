using RunFence.Core.Models;

namespace RunFence.Acl.UI;

public sealed class AclManagerPendingGrantMutations : AclManagerPendingGrantState
{
    private readonly PendingPathChangeCollection<(string Path, bool IsDeny)> _grantChanges;
    private readonly Dictionary<(string Path, bool IsDeny), PendingModification> _pendingModifications;

    internal AclManagerPendingGrantMutations(
        PendingPathChangeCollection<(string Path, bool IsDeny)> grantChanges,
        Dictionary<(string Path, bool IsDeny), PendingModification> pendingModifications)
        : base(grantChanges, pendingModifications)
    {
        _grantChanges = grantChanges;
        _pendingModifications = pendingModifications;
    }

    public void AddGrant(GrantedPathEntry entry)
        => _grantChanges.Add(entry);

    public bool RemoveGrant(string path, bool isDeny)
        => _grantChanges.RemoveAdd(AclPendingKeys.Grant(path, isDeny));

    public void MarkGrantForRemoval(GrantedPathEntry entry)
        => _grantChanges.AddRemoval(entry);

    public bool CancelGrantRemoval(string path, bool isDeny)
        => _grantChanges.RemoveRemoval(AclPendingKeys.Grant(path, isDeny));

    public void ModifyGrant(GrantedPathEntry entry, PendingModification modification)
        => _pendingModifications[AclPendingKeys.Grant(entry)] = modification;

    public bool RemoveGrantModification(string path, bool isDeny, out PendingModification? modification)
    {
        if (_pendingModifications.Remove(AclPendingKeys.Grant(path, isDeny), out var removed))
        {
            modification = removed;
            return true;
        }

        modification = null;
        return false;
    }

    public void AddGrantFix(GrantedPathEntry entry)
        => _grantChanges.AddFix(entry);

    public bool RemoveGrantFix(string path, bool isDeny)
        => _grantChanges.RemoveFix(AclPendingKeys.Grant(path, isDeny));

    public void UntrackGrant(GrantedPathEntry entry)
        => _grantChanges.AddUntrack(entry);

    public bool RemoveUntrackedGrant(string path, bool isDeny)
        => _grantChanges.RemoveUntrack(AclPendingKeys.Grant(path, isDeny));

    public void MoveGrantConfig(GrantedPathEntry entry, string? targetConfigPath)
        => _grantChanges.AddConfigMove(entry, targetConfigPath);

    public bool RemoveGrantConfigMove(string path, bool isDeny, out PendingConfigMove? move)
        => _grantChanges.RemoveConfigMove(AclPendingKeys.Grant(path, isDeny), out move);

    public bool RekeyGrantConfigMove(string path, bool isDeny, GrantedPathEntry updatedEntry)
        => _grantChanges.RekeyConfigMove(AclPendingKeys.Grant(path, isDeny), updatedEntry);
}
