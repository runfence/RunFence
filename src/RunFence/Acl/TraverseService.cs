using RunFence.Core.Models;
using RunFence.Persistence;
using RunFence.Acl.Traverse;

namespace RunFence.Acl;

public class TraverseService(
    ITraverseCoreOperations traverseCore,
    TraverseRestoreWorkflow traverseRestoreWorkflow,
    PersistedTraverseMutationWorkflow persistedTraverseMutationWorkflow) : ITraverseService
{
    public GrantApplyResult AddTraverse(string accountSid, string path, IGrantIntentStore? store = null)
        => persistedTraverseMutationWorkflow.AddTraverse(accountSid, path, store);

    public GrantApplyResult RemoveTraverse(string accountSid, string path)
        => persistedTraverseMutationWorkflow.RemoveTraverse(accountSid, path);

    public GrantApplyResult RestoreTraverse(string accountSid, string path,
        GrantIntentRestoreSnapshot previousState)
        => traverseRestoreWorkflow.Restore(accountSid, Path.GetFullPath(path), previousState);

    public GrantApplyResult UntrackTraverse(string accountSid, string path)
        => persistedTraverseMutationWorkflow.UntrackTraverse(accountSid, path);

    public void CleanupOrphanedTraverse(string sid, string path)
        => traverseCore.CleanupOrphanedTraverse(sid, Path.GetFullPath(path));

    public List<string> FixTraverse(string sid, string path)
        => traverseCore.FixTraverse(sid, path);

    public GrantApplyResult FixTraverseAcl(string accountSid, string path)
        => persistedTraverseMutationWorkflow.FixTraverseAcl(accountSid, path);
}
