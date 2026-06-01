using RunFence.Core.Models;

namespace RunFence.Acl.UI;

public sealed class AclManagerPendingTraverseMutations : AclManagerPendingTraverseState
{
    private readonly PendingPathChangeCollection<string> _traverseChanges;

    internal AclManagerPendingTraverseMutations(PendingPathChangeCollection<string> traverseChanges)
        : base(traverseChanges)
    {
        _traverseChanges = traverseChanges;
    }

    public void AddTraverse(GrantedPathEntry entry)
        => _traverseChanges.Add(entry);

    public bool RemoveTraverse(string path)
        => _traverseChanges.RemoveAdd(AclPendingKeys.Traverse(path));

    public void MarkTraverseForRemoval(GrantedPathEntry entry)
        => _traverseChanges.AddRemoval(entry);

    public bool CancelTraverseRemoval(string path)
        => _traverseChanges.RemoveRemoval(AclPendingKeys.Traverse(path));

    public void AddTraverseFix(GrantedPathEntry entry)
        => _traverseChanges.AddFix(entry);

    public bool RemoveTraverseFix(string path)
        => _traverseChanges.RemoveFix(AclPendingKeys.Traverse(path));

    public void UntrackTraverse(GrantedPathEntry entry)
        => _traverseChanges.AddUntrack(entry);

    public bool RemoveUntrackedTraverse(string path)
        => _traverseChanges.RemoveUntrack(AclPendingKeys.Traverse(path));

    public void MoveTraverseConfig(GrantedPathEntry entry, string? targetConfigPath)
        => _traverseChanges.AddConfigMove(entry, targetConfigPath);

    public bool RemoveTraverseConfigMove(string path, out PendingConfigMove? move)
        => _traverseChanges.RemoveConfigMove(AclPendingKeys.Traverse(path), out move);
}
