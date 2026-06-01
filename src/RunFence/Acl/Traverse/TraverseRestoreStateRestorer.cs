using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Persistence;

namespace RunFence.Acl.Traverse;

public sealed class TraverseRestoreStateRestorer(
    GrantRuntimeSnapshotService grantRuntimeSnapshotService,
    TraverseGrantStateService traverseGrantStateService,
    IGrantIntentStoreSaveService grantIntentStoreSaveService)
{
    public sealed record TraverseStoreSnapshot(
        string OwnerSid,
        string Path,
        IReadOnlyList<TraverseGrantStateService.StoreSnapshot> StoreSnapshots);

    public GrantRuntimeEntrySnapshot CaptureRuntimeSnapshot(string sid, string normalizedPath)
        => grantRuntimeSnapshotService.CaptureTraverseSnapshot(sid, normalizedPath);

    public void TryRestoreRuntimeTraverseEntry(
        GrantRuntimeEntrySnapshot snapshot,
        GrantOperationException operationException)
    {
        try
        {
            grantRuntimeSnapshotService.RestoreTraverseSnapshot(snapshot);
        }
        catch (Exception ex)
        {
            operationException.AppendCleanupFailure(
                GrantApplyFailureStep.RevertIntentSave,
                snapshot.Path,
                null,
                ex);
        }
    }

    public TraverseStoreSnapshot CaptureStoreSnapshots(
        string ownerSid,
        string normalizedPath,
        IEnumerable<IGrantIntentStore> stores)
        => new(
            ownerSid,
            normalizedPath,
            traverseGrantStateService.CaptureStoreSnapshots(ownerSid, normalizedPath, stores));

    public void TryRestoreStoreSnapshots(
        TraverseStoreSnapshot snapshot,
        GrantOperationException operationException)
    {
        var restorePath = snapshot.Path;
        if (string.IsNullOrEmpty(restorePath))
        {
            operationException.AppendCleanupFailure(
                GrantApplyFailureStep.RevertIntentSave,
                operationException.Path,
                grantIntentStoreSaveService.GetPrimaryConfigPath(snapshot.StoreSnapshots.Select(storeSnapshot => storeSnapshot.Store)),
                new InvalidOperationException("Traverse restore snapshots must include a path."));
            return;
        }

        try
        {
            traverseGrantStateService.RestoreStoreSnapshots(snapshot.OwnerSid, snapshot.Path, snapshot.StoreSnapshots);

            grantIntentStoreSaveService.Save(
                snapshot.StoreSnapshots.Select(storeSnapshot => storeSnapshot.Store),
                GrantApplyFailureStep.RevertIntentSave,
                restorePath);
        }
        catch (GrantOperationException ex)
        {
            operationException.AppendCleanupFailure(ex.Step, ex.Path, ex.ConfigPath, ex.Cause);
            operationException.AppendCleanupFailures(ex.CleanupFailures);
        }
        catch (Exception ex)
        {
            operationException.AppendCleanupFailure(
                GrantApplyFailureStep.RevertIntentSave,
                restorePath,
                grantIntentStoreSaveService.GetPrimaryConfigPath(snapshot.StoreSnapshots.Select(storeSnapshot => storeSnapshot.Store)),
                ex);
        }
    }
}
