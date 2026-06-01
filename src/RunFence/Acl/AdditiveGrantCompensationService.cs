using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Persistence;

namespace RunFence.Acl;

public sealed class AdditiveGrantCompensationService(
    IPathSecurityDescriptorAccessor aclAccessor,
    IFileSystemPathInfo pathInfo,
    Func<IGrantIntentStoreProvider> grantIntentStoreProvider,
    GrantIntentStoreMutationService grantIntentStoreMutationService,
    GrantRuntimeSnapshotService grantRuntimeSnapshotService,
    IGrantIntentStoreSaveService grantIntentStoreSaveService,
    GrantAclRollbackService grantAclRollbackService)
{
    public sealed class AdditiveGrantCompensationContext
    {
        public AdditiveGrantCompensationContext(
            GrantIntentRestoreSnapshot restoreSnapshot,
            bool isDeny,
            bool restoreStoreIntent,
            AdditiveGrantSideEffectSnapshot sideEffectSnapshot)
        {
            RestoreSnapshot = restoreSnapshot;
            IsDeny = isDeny;
            RestoreStoreIntent = restoreStoreIntent;
            SideEffectSnapshot = sideEffectSnapshot;
        }

        public GrantIntentRestoreSnapshot RestoreSnapshot { get; }

        public bool IsDeny { get; }

        public bool RestoreStoreIntent { get; }

        public AdditiveGrantSideEffectSnapshot SideEffectSnapshot { get; }
    }

    public sealed class AdditiveGrantSideEffectSnapshot
    {
        public AdditiveGrantSideEffectSnapshot(
            GrantRuntimeEntrySnapshot? traverseSnapshot,
            GrantRuntimeEntrySnapshot? lowIntegrityGrantSnapshot,
            IReadOnlyList<GrantRuntimeEntrySnapshot> linkedGrantSnapshots,
            IReadOnlyList<GrantRuntimeEntrySnapshot> linkedTraverseSnapshots)
        {
            TraverseSnapshot = traverseSnapshot;
            LowIntegrityGrantSnapshot = lowIntegrityGrantSnapshot;
            LinkedGrantSnapshots = linkedGrantSnapshots.ToList();
            LinkedTraverseSnapshots = linkedTraverseSnapshots.ToList();
        }

        public GrantRuntimeEntrySnapshot? TraverseSnapshot { get; }

        public GrantRuntimeEntrySnapshot? LowIntegrityGrantSnapshot { get; }

        public IReadOnlyList<GrantRuntimeEntrySnapshot> LinkedGrantSnapshots { get; }

        public IReadOnlyList<GrantRuntimeEntrySnapshot> LinkedTraverseSnapshots { get; }
    }

    private IGrantIntentStoreProvider GrantIntentStoreProvider => grantIntentStoreProvider();

    public AdditiveGrantCompensationContext Create(
        string sid,
        bool isDeny,
        IReadOnlyList<GrantIntentStoreSnapshot> snapshots,
        string normalizedPath,
        bool storeModified)
    {
        var locations = snapshots
            .SelectMany(snapshot => snapshot.Entries.Select(entry =>
                new GrantIntentRestoreLocation(
                    new GrantIntentStoreIdentity(snapshot.Store.ConfigPath),
                    entry)))
            .ToList();
        var runtimeEntry = grantRuntimeSnapshotService.CaptureGrantSnapshot(sid, normalizedPath, isDeny).Entry;
        var restoreSnapshot = new GrantIntentRestoreSnapshot(
            runtimeEntry,
            locations,
            previousTargetSecurity: aclAccessor.GetSecurity(normalizedPath));
        var sideEffectSnapshot = new AdditiveGrantSideEffectSnapshot(
            traverseSnapshot: null,
            lowIntegrityGrantSnapshot: null,
            linkedGrantSnapshots: [],
            linkedTraverseSnapshots: []);
        if (isDeny)
            return new AdditiveGrantCompensationContext(restoreSnapshot, isDeny, storeModified, sideEffectSnapshot);

        var traversePath = pathInfo.DirectoryExists(normalizedPath)
            ? normalizedPath
            : Path.GetDirectoryName(normalizedPath);
        GrantRuntimeEntrySnapshot? traverseSnapshot = null;
        if (!string.IsNullOrEmpty(traversePath))
            traverseSnapshot = grantRuntimeSnapshotService.CaptureTraverseSnapshot(sid, traversePath);

        GrantRuntimeEntrySnapshot? lowIntegrityGrantSnapshot = null;
        IReadOnlyList<GrantRuntimeEntrySnapshot> linkedGrantSnapshots = [];
        IReadOnlyList<GrantRuntimeEntrySnapshot> linkedTraverseSnapshots = [];

        if (AclHelper.IsContainerSid(sid))
        {
            linkedGrantSnapshots = grantRuntimeSnapshotService.CaptureEntrySnapshotsForPath(
                normalizedPath,
                isTraverseOnly: false);
            if (!string.IsNullOrEmpty(traversePath))
            {
                linkedTraverseSnapshots = grantRuntimeSnapshotService.CaptureEntrySnapshotsForPath(
                    traversePath,
                    isTraverseOnly: true);
            }
        }
        else if (!AclHelper.IsLowIntegritySid(sid))
        {
            lowIntegrityGrantSnapshot = grantRuntimeSnapshotService.CaptureGrantSnapshot(
                AclHelper.LowIntegritySid,
                normalizedPath,
                isDeny: false);
        }

        sideEffectSnapshot = new AdditiveGrantSideEffectSnapshot(
            traverseSnapshot,
            lowIntegrityGrantSnapshot,
            linkedGrantSnapshots,
            linkedTraverseSnapshots);

        return new AdditiveGrantCompensationContext(restoreSnapshot, isDeny, storeModified, sideEffectSnapshot);
    }

    public bool TryRestoreSavedIntent(
        string sid,
        string normalizedPath,
        AdditiveGrantCompensationContext compensation,
        GrantOperationException operationException)
    {
        try
        {
            grantRuntimeSnapshotService.RestoreGrantSnapshot(
                new GrantRuntimeEntrySnapshot(
                    sid,
                    normalizedPath,
                    isTraverseOnly: false,
                    isDeny: compensation.RestoreSnapshot.RuntimeEntry?.IsDeny ?? compensation.IsDeny,
                    compensation.RestoreSnapshot.RuntimeEntry));

            if (compensation.RestoreStoreIntent)
            {
                var currentLocations = grantIntentStoreMutationService.GetGrantLocationsForPath(sid, normalizedPath);
                var affectedStores = currentLocations.Select(location => location.Store)
                    .Concat(compensation.RestoreSnapshot.Locations
                        .Select(location => GrantIntentStoreProvider.ResolveStore(location.StoreIdentity.ConfigPath)))
                    .Distinct()
                    .ToList();
                grantIntentStoreMutationService.RestoreGrantStoresToExactLocations(
                    sid,
                    currentLocations,
                    compensation.RestoreSnapshot.Locations,
                    mutate: true);
                grantIntentStoreSaveService.Save(
                    affectedStores,
                    GrantApplyFailureStep.RevertIntentSave,
                    normalizedPath);
            }

            return true;
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
                normalizedPath,
                grantIntentStoreSaveService.GetPrimaryConfigPath(
                    compensation.RestoreSnapshot.Locations
                        .Select(location => GrantIntentStoreProvider.ResolveStore(location.StoreIdentity.ConfigPath))),
                ex);
        }

        return false;
    }

    public void RestoreSideEffectsAfterTargetSecurityRestore(
        string accountSid,
        string normalizedPath,
        AdditiveGrantCompensationContext compensation,
        GrantOperationException operationException)
    {
        try
        {
            var sideEffectSnapshot = compensation.SideEffectSnapshot;
            if (sideEffectSnapshot.TraverseSnapshot is { } traverseSnapshot)
            {
                grantAclRollbackService.TryRollbackTraverseAcesAfterSnapshotRestore(
                    traverseSnapshot,
                    normalizedPath,
                    operationException);
                grantRuntimeSnapshotService.RestoreTraverseSnapshot(traverseSnapshot);
            }

            if (sideEffectSnapshot.LowIntegrityGrantSnapshot is { } lowIntegrityGrantSnapshot)
                grantRuntimeSnapshotService.RestoreGrantSnapshot(lowIntegrityGrantSnapshot);

            if (AclHelper.IsContainerSid(accountSid))
            {
                grantRuntimeSnapshotService.RestoreLinkedEntrySnapshots(
                    normalizedPath,
                    isTraverseOnly: false,
                    accountSid,
                    sideEffectSnapshot.LinkedGrantSnapshots);

                if (sideEffectSnapshot.TraverseSnapshot is { Path: var traversePath })
                {
                    foreach (var linkedTraverseSnapshot in sideEffectSnapshot.LinkedTraverseSnapshots)
                    {
                        grantAclRollbackService.TryRollbackTraverseAcesAfterSnapshotRestore(
                            linkedTraverseSnapshot,
                            normalizedPath,
                            operationException);
                    }

                    grantRuntimeSnapshotService.RestoreLinkedEntrySnapshots(
                        traversePath,
                        isTraverseOnly: true,
                        accountSid,
                        sideEffectSnapshot.LinkedTraverseSnapshots);
                }
            }
        }
        catch (Exception ex)
        {
            operationException.AppendCleanupFailure(
                GrantApplyFailureStep.GrantAclRollback,
                normalizedPath,
                null,
                ex);
        }
    }
}
