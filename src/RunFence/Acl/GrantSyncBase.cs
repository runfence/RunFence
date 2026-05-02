using RunFence.Infrastructure;

namespace RunFence.Acl;

/// <summary>
/// Shared base for synchronization helpers that react to grant changes by removing
/// derived grants and cleaning up orphaned traverse entries.
/// </summary>
public abstract class GrantSyncBase(
    IGrantCoreOperations grantCore,
    ITraverseCoreOperations traverseCore,
    UiThreadDatabaseAccessor dbAccessor)
{
    protected IGrantCoreOperations GrantCore { get; } = grantCore;
    protected ITraverseCoreOperations TraverseCore { get; } = traverseCore;
    protected UiThreadDatabaseAccessor DbAccessor { get; } = dbAccessor;

    /// <summary>
    /// Removes the grant for <paramref name="sid"/> at <paramref name="path"/> from DB and,
    /// when <paramref name="updateFileSystem"/> is true, from NTFS. Then cleans up orphaned
    /// traverse grants. Calls <paramref name="onRemoved"/> after removal.
    /// No-op if no grant exists.
    /// </summary>
    protected void RemoveGrantWithCleanup(
        string sid,
        string path,
        bool updateFileSystem,
        Action? onRemoved = null)
    {
        var result = GrantCore.RemoveGrant(sid, path, isDeny: false, updateFileSystem);
        if (!result.Found) return;
        TraverseCore.CleanupOrphanedTraverse(sid, path);
        onRemoved?.Invoke();
    }
}
