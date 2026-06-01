using System.Security.Principal;
using RunFence.Infrastructure;

namespace RunFence.Acl.Traverse;

public sealed class TraverseRestoreAclRollbackService(
    IFileSystemPathInfo pathInfo,
    ITraverseAcl traverseAcl,
    ITraverseCoreOperations traverseCore,
    ITraverseIntentStoreCoordinator traverseIntentStoreCoordinator)
{
    public List<string> CaptureExplicitTraverseAclPaths(string sid, IReadOnlyList<string> candidatePaths)
    {
        if (candidatePaths.Count == 0)
            return [];

        var traverseSid = traverseIntentStoreCoordinator.ResolveAclSid(sid);
        var identity = new SecurityIdentifier(traverseSid);
        var explicitPaths = new List<string>();

        foreach (var path in candidatePaths)
        {
            if (HasExplicitTraverseAcl(path, identity))
                explicitPaths.Add(path);
        }

        return explicitPaths;
    }

    public void TryRollbackTraverseAcl(
        string sid,
        IReadOnlyList<string> appliedPaths,
        string? primaryConfigPath,
        GrantOperationException operationException)
    {
        if (appliedPaths.Count == 0)
            return;

        try
        {
            traverseCore.RemoveTraverseAces(sid, appliedPaths);
        }
        catch (Exception ex)
        {
            operationException.AppendCleanupFailure(
                GrantApplyFailureStep.TraverseAclRollback,
                appliedPaths[0],
                primaryConfigPath,
                ex);
        }
    }

    public void TryReapplyRemovedTraverseAcl(
        string sid,
        IReadOnlyList<string> pathsToRestore,
        string normalizedPath,
        string? primaryConfigPath,
        GrantOperationException operationException)
    {
        if (pathsToRestore.Count == 0)
            return;

        try
        {
            var traverseSid = traverseIntentStoreCoordinator.ResolveAclSid(sid);
            var identity = new SecurityIdentifier(traverseSid);
            var missingPaths = pathsToRestore
                .Where(path => !HasExplicitTraverseAcl(path, identity))
                .ToList();
            if (missingPaths.Count == 0)
                return;

            traverseCore.ApplyTraverseAces(sid, missingPaths);
        }
        catch (Exception ex)
        {
            operationException.AppendCleanupFailure(
                GrantApplyFailureStep.TraverseAclRollback,
                normalizedPath,
                primaryConfigPath,
                ex);
        }
    }

    public bool HasExplicitTraverseAcl(string path, SecurityIdentifier identity)
    {
        if (!pathInfo.DirectoryExists(path))
            return false;

        return traverseAcl.HasExplicitTraverseAceOrThrow(path, identity);
    }
}
