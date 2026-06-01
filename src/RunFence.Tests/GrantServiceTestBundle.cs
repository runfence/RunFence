using RunFence.Acl;

namespace RunFence.Tests;

internal sealed class GrantServiceTestBundle(
    IGrantMutatorService grantMutator,
    ITraverseService traverse,
    IGrantInspectionService inspection,
    IGrantIntentSnapshotService snapshots,
    IGrantSyncService sync,
    IGrantAccountCleanupService accountCleanup,
    GrantFileSystemOperations fileSystemOperations)
{
    public IGrantMutatorService GrantMutator { get; } = grantMutator;
    public ITraverseService Traverse { get; } = traverse;
    public IGrantInspectionService Inspection { get; } = inspection;
    public IGrantIntentSnapshotService Snapshots { get; } = snapshots;
    public IGrantSyncService Sync { get; } = sync;
    public IGrantAccountCleanupService AccountCleanup { get; } = accountCleanup;
    public GrantFileSystemOperations FileSystemOperations { get; } = fileSystemOperations;
}
