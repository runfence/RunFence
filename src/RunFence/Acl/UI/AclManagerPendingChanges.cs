namespace RunFence.Acl.UI;

public class AclManagerPendingChanges
{
    private readonly PendingPathChangeCollection<(string Path, bool IsDeny)> _grantChanges =
        new(AclPendingKeys.Grant, new GrantPathKeyComparer());
    private readonly Dictionary<(string Path, bool IsDeny), PendingModification> _pendingGrantModifications =
        new(new GrantPathKeyComparer());
    private readonly PendingPathChangeCollection<string> _traverseChanges =
        new(AclPendingKeys.Traverse, StringComparer.OrdinalIgnoreCase);

    public AclManagerPendingGrantMutations Grants { get; }

    public AclManagerPendingTraverseMutations Traverse { get; }

    public AclManagerPendingChanges()
    {
        Grants = new AclManagerPendingGrantMutations(_grantChanges, _pendingGrantModifications);
        Traverse = new AclManagerPendingTraverseMutations(_traverseChanges);
    }

    public bool HasPendingChanges =>
        Grants.HasPendingChanges ||
        Traverse.HasPendingChanges;

    public void Clear()
    {
        Grants.Clear();
        Traverse.Clear();
    }

    public AclManagerPendingChangesSnapshot CaptureSnapshot()
    {
        var grantSnapshot = Grants.GetSnapshot();
        var traverseSnapshot = Traverse.GetSnapshot();
        return new(
            grantSnapshot.PendingAdds,
            grantSnapshot.PendingRemoves,
            grantSnapshot.PendingModifications,
            grantSnapshot.PendingGrantFixes,
            traverseSnapshot.PendingAdds,
            traverseSnapshot.PendingRemoves,
            traverseSnapshot.PendingFixes,
            grantSnapshot.PendingUntrack,
            traverseSnapshot.PendingUntrack,
            grantSnapshot.PendingConfigMoves,
            traverseSnapshot.PendingConfigMoves);
    }

    public void RestoreFromSnapshot(AclManagerPendingChangesSnapshot snapshot)
    {
        Grants.RestoreFromSnapshot(new AclGrantPendingChangesSnapshot(
            snapshot.PendingAdds,
            snapshot.PendingRemoves,
            snapshot.PendingModifications,
            snapshot.PendingGrantFixes,
            snapshot.PendingUntrackGrants,
            snapshot.PendingConfigMoves));
        Traverse.RestoreFromSnapshot(new AclTraversePendingChangesSnapshot(
            snapshot.PendingTraverseAdds,
            snapshot.PendingTraverseRemoves,
            snapshot.PendingTraverseFixes,
            snapshot.PendingUntrackTraverse,
            snapshot.PendingTraverseConfigMoves));
    }
}
