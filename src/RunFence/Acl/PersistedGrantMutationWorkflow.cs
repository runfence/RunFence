using RunFence.Acl.Permissions;
using RunFence.Acl.Traverse;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Persistence;

namespace RunFence.Acl;

public class PersistedGrantMutationWorkflow(
    ITraverseCoreOperations traverseCore,
    IMandatoryLabelService mandatoryLabelService,
    GrantFileSystemOperations fileSystemOperations,
    IGrantAceService grantAceService,
    IFileSystemPathInfo pathInfo,
    ITraverseIntentStoreCoordinator traverseIntentStoreCoordinator,
    TraverseGrantStateService traverseGrantStateService,
    Func<IGrantIntentStoreProvider> grantIntentStoreProvider,
    GrantMutationOrderResolver grantMutationOrderResolver,
    GrantIntentMutationStateRestorer grantIntentMutationStateRestorer,
    GrantAclRollbackService grantAclRollbackService,
    AdditiveGrantCompensationService additiveGrantCompensationService,
    GrantIntentStoreMutationService grantIntentStoreMutationService,
    GrantRuntimeMutationService grantRuntimeMutationService,
    GrantRuntimeSnapshotService grantRuntimeSnapshotService,
    IGrantIntentStoreSaveService grantIntentStoreSaveService)
{
    internal readonly record struct GrantMutationPendingSave(
        GrantApplyResult Result,
        IReadOnlyList<IGrantIntentStore> AffectedStores,
        string SavePath);
    private IGrantIntentStoreProvider GrantIntentStoreProvider => grantIntentStoreProvider();
    public GrantApplyResult AddGrant(
        string accountSid,
        string path,
        bool isDeny,
        SavedRightsState requestedRights,
        Func<bool>? confirm,
        IGrantIntentStore? store)
        => PersistGrantChange(
            accountSid,
            path,
            newIsDeny: isDeny,
            requestedRights,
            confirm,
            store,
            allowOppositeModeSwitch: false,
            forceRuntimeApply: true,
            preferExistingAddSemantics: true);

    public GrantApplyResult UpdateGrant(
        string accountSid,
        string path,
        bool isDeny,
        SavedRightsState requestedRights,
        Func<bool>? confirm,
        IGrantIntentStore? store)
        => PersistGrantChange(
            accountSid,
            path,
            newIsDeny: isDeny,
            requestedRights,
            confirm,
            store,
            allowOppositeModeSwitch: false,
            forceRuntimeApply: true,
            preferExistingAddSemantics: false);

    public GrantApplyResult SwitchGrantMode(
        string accountSid,
        string path,
        bool newIsDeny,
        SavedRightsState requestedRights,
        Func<bool>? confirm,
        IGrantIntentStore? store)
        => PersistGrantChange(
            accountSid,
            path,
            newIsDeny,
            requestedRights,
            confirm,
            store,
            allowOppositeModeSwitch: true,
            forceRuntimeApply: true,
            preferExistingAddSemantics: false);

    public GrantApplyResult RemoveGrant(string accountSid, string path, bool isDeny)
    {
        var normalized = Path.GetFullPath(path);
        var existingLocations = GetGrantLocationsForPath(accountSid, normalized)
            .Where(location => location.Entry.IsDeny == isDeny)
            .ToList();
        if (existingLocations.Count == 0)
            return default;

        var primaryConfigPath = grantIntentStoreSaveService.GetPrimaryConfigPath(existingLocations.Select(location => location.Store));
        var previousEntry = existingLocations[0].Entry;

        try
        {
            if (!grantRuntimeMutationService.RemoveGrantWithoutPersisting(accountSid, normalized, isDeny, updateFileSystem: true))
            {
                grantRuntimeMutationService.RemoveGrantAclOnly(
                    accountSid,
                    normalized,
                    isDeny,
                    previousEntry.SavedRights,
                    previousEntry.PreviousSaclLabel);
            }
        }
        catch (Exception ex)
        {
            throw new GrantOperationException(
                GrantApplyFailureStep.GrantAclRemove,
                normalized,
                primaryConfigPath,
                ex);
        }

        RemoveGrantEntries(accountSid, normalized, existingLocations);
        var warnings = grantIntentStoreSaveService.SaveWithWarnings(
            existingLocations.Select(location => location.Store),
            GrantApplyFailureStep.PostGrantRemoveSave,
            normalized);

        return new GrantApplyResult(
            GrantApplied: true,
            DatabaseModified: true,
            DurableSaveCompleted: warnings.Count == 0,
            Warnings: warnings);
    }

    public GrantApplyResult RestoreGrant(string accountSid, string path, bool isDeny, GrantIntentRestoreSnapshot previousState)
    {
        var normalized = Path.GetFullPath(path);
        if (previousState.RuntimeEntry == null && previousState.Locations.Count == 0)
            return RemoveGrant(accountSid, normalized, isDeny);

        var previousEntry = previousState.RuntimeEntry ?? previousState.Locations[0].Entry;

        if (previousEntry.IsTraverseOnly)
            throw new InvalidOperationException("RestoreGrant only supports non-traverse grant entries.");

        if (previousEntry.IsDeny != isDeny)
            throw new InvalidOperationException("RestoreGrant mode must match the previous entry.");

        var restoredEntry = previousEntry.Clone();
        restoredEntry.Path = normalized;
        restoredEntry.IsTraverseOnly = false;
        restoredEntry.IsDeny = isDeny;

        var allLocations = GetGrantLocationsForPath(accountSid, normalized);
        var sameModeLocations = allLocations
            .Where(location => location.Entry.IsDeny == isDeny)
            .ToList();
        var oppositeModeLocations = allLocations
            .Where(location => location.Entry.IsDeny != isDeny)
            .ToList();
        if (oppositeModeLocations.Count > 0)
        {
            throw new InvalidOperationException(
                $"An opposite-mode grant for '{normalized}' already exists for SID '{accountSid}'.");
        }

        var finalStores = previousState.Locations
            .Select(location => GrantIntentStoreProvider.ResolveStore(location.StoreIdentity.ConfigPath))
            .Distinct()
            .ToList();
        var affectedStores = allLocations.Select(location => location.Store)
            .Concat(finalStores)
            .Distinct()
            .ToList();
        var snapshots = grantIntentMutationStateRestorer.CaptureStoreSnapshots(
            affectedStores.Select(store => (store, accountSid)),
            normalized,
            traversePath: null,
            includeDeny: true);
        var primaryConfigPath = grantIntentStoreSaveService.GetPrimaryConfigPath(affectedStores);
        var currentEntry = sameModeLocations.FirstOrDefault()?.Entry;
        bool storeModified = RestoreGrantStoresToExactLocations(
            accountSid,
            allLocations,
            previousState.Locations,
            mutate: false);
        bool targetAclChange = RequiresTargetAclChange(
            currentEntry,
            restoredEntry,
            allowOppositeModeSwitch: false,
            hasOppositeModeEntry: false);
        if (!storeModified && !targetAclChange)
            return default;

        return ExecuteGrantMutation(
            accountSid,
            normalized,
            currentEntry,
            restoredEntry,
            affectedStores,
            snapshots,
            primaryConfigPath,
            storeModified,
            targetAclChange,
            preferExistingAddSemantics: false,
            () => RestoreGrantStoresToExactLocations(
                accountSid,
                allLocations,
                previousState.Locations,
                mutate: true));
    }

    public GrantApplyResult UntrackGrant(string accountSid, string path, bool isDeny)
    {
        var normalized = Path.GetFullPath(path);
        var existingLocations = GetGrantLocationsForPath(accountSid, normalized)
            .Where(location => location.Entry.IsDeny == isDeny)
            .ToList();
        if (existingLocations.Count == 0)
            return default;

        RemoveTrackedGrantWithoutFilesystem(
            accountSid,
            normalized,
            existingLocations[0].Entry);
        RemoveGrantEntries(accountSid, normalized, existingLocations);
        var warnings = grantIntentStoreSaveService.SaveWithWarnings(
            existingLocations.Select(location => location.Store),
            GrantApplyFailureStep.UntrackGrantSave,
            normalized);

        return new GrantApplyResult(
            DatabaseModified: true,
            DurableSaveCompleted: warnings.Count == 0,
            Warnings: warnings);
    }

    public GrantApplyResult RemoveAllGrants(string accountSid)
    {
        var mutation = RemoveAllGrantsWithoutSaving(accountSid);
        if (!mutation.Result.DatabaseModified)
            return default;

        var warnings = grantIntentStoreSaveService.SaveWithWarnings(
            mutation.AffectedStores,
            GrantApplyFailureStep.PostRemoveAllSave,
            mutation.SavePath);

        return new GrantApplyResult(
            GrantApplied: mutation.Result.GrantApplied,
            DatabaseModified: true,
            DurableSaveCompleted: warnings.Count == 0,
            Warnings: warnings);
    }

    internal GrantMutationPendingSave RemoveAllGrantsWithoutSaving(string accountSid)
    {
        var grantLocations = GetAllGrantLocations(accountSid);
        if (grantLocations.Count == 0)
            return default;

        var affectedStores = grantLocations.Select(location => location.Store)
            .Distinct()
            .ToList();
        var primaryConfigPath = grantIntentStoreSaveService.GetPrimaryConfigPath(affectedStores);

        foreach (var grantGroup in GroupGrantLocationsByPath(grantLocations))
        {
            try
            {
                var normalizedPath = Path.GetFullPath(grantGroup.Path);
                grantAceService.RevertAce(normalizedPath, accountSid, grantGroup.IsDeny);

                if (!grantGroup.IsDeny &&
                    AclHelper.IsLowIntegritySid(accountSid) &&
                    grantGroup.Entry.SavedRights?.Write == true)
                {
                    mandatoryLabelService.RestoreMandatoryLabel(normalizedPath, grantGroup.Entry.PreviousSaclLabel);
                }
            }
            catch (Exception ex)
            {
                throw new GrantOperationException(
                    GrantApplyFailureStep.RemoveAllGrantAclRemove,
                    grantGroup.Path,
                    primaryConfigPath,
                    ex);
            }
        }

        var trackedTraversePaths = traverseIntentStoreCoordinator.GetAllTraverseLocations(accountSid)
            .Select(location => location.Entry.Path)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var remainingTraverseEntries = traverseGrantStateService.GetRemainingTraverseEntriesForCleanup(accountSid, []);
        var grantPaths = traverseGrantStateService.GetTraverseGrantPathsForCleanup(accountSid, grantLocations);

        foreach (var grantGroup in GroupGrantLocationsByPath(grantLocations))
        {
            RemoveRuntimeGrant(accountSid, grantGroup.Path, grantGroup.Entry, updateFileSystem: false);
            if (!grantGroup.Entry.IsDeny)
            {
                var normalizedGrantPath = Path.GetFullPath(grantGroup.Path);
                var traversePath = pathInfo.DirectoryExists(normalizedGrantPath)
                    ? normalizedGrantPath
                    : Path.GetDirectoryName(normalizedGrantPath);
                if (!string.IsNullOrEmpty(traversePath) &&
                    !trackedTraversePaths.Contains(traversePath) &&
                    !grantPaths.Contains(traversePath) &&
                    !remainingTraverseEntries
                        .Where(entry => !string.Equals(entry.Path, traversePath, StringComparison.OrdinalIgnoreCase))
                        .Any(entry => traverseGrantStateService.CollectStoredTraversePaths(entry)
                        .Contains(traversePath, StringComparer.OrdinalIgnoreCase)))
                {
                    traverseCore.CleanupOrphanedTraverse(accountSid, traversePath);
                }
            }

            RemoveGrantEntries(accountSid, grantGroup.Path, grantGroup.Locations);
        }

        return new GrantMutationPendingSave(
            new GrantApplyResult(
                GrantApplied: true,
                DatabaseModified: true),
            affectedStores,
            grantLocations[0].Entry.Path);
    }

    public GrantApplyResult UntrackAllGrants(string accountSid)
    {
        var mutation = UntrackAllGrantsWithoutSaving(accountSid);
        if (!mutation.Result.DatabaseModified)
            return default;

        var warnings = grantIntentStoreSaveService.SaveWithWarnings(
            mutation.AffectedStores,
            GrantApplyFailureStep.UntrackAllSave,
            mutation.SavePath);

        return new GrantApplyResult(
            DatabaseModified: true,
            DurableSaveCompleted: warnings.Count == 0,
            Warnings: warnings);
    }

    internal GrantMutationPendingSave UntrackAllGrantsWithoutSaving(string accountSid)
    {
        var grantLocations = GetAllGrantLocations(accountSid);
        if (grantLocations.Count == 0)
            return default;

        foreach (var grantGroup in GroupGrantLocationsByPath(grantLocations))
        {
            RemoveTrackedGrantWithoutFilesystem(
                accountSid,
                grantGroup.Path,
                grantGroup.Entry);
            RemoveGrantEntries(accountSid, grantGroup.Path, grantGroup.Locations);
        }

        return new GrantMutationPendingSave(
            new GrantApplyResult(DatabaseModified: true),
            grantLocations.Select(location => location.Store).Distinct().ToList(),
            grantLocations[0].Entry.Path);
    }

    public GrantApplyResult FixGrantAcl(string accountSid, string path, bool isDeny)
    {
        var normalized = Path.GetFullPath(path);
        var existingLocation = GetGrantLocationsForPath(accountSid, normalized)
            .Where(location => location.Entry.IsDeny == isDeny)
            .ToList();
        if (existingLocation.Count == 0)
            return default;

        var entry = existingLocation[0].Entry;
        string? traversePath = null;
        GrantRuntimeEntrySnapshot? traverseSnapshot = null;
        GrantRuntimeEntrySnapshot? lowIntegritySnapshot = null;
        IReadOnlyList<GrantRuntimeEntrySnapshot> containerGrantSnapshots = [];
        IReadOnlyList<GrantRuntimeEntrySnapshot> containerTraverseSnapshots = [];
        if (!isDeny)
        {
            traversePath = pathInfo.DirectoryExists(normalized) ? normalized : Path.GetDirectoryName(normalized);
            if (!string.IsNullOrEmpty(traversePath))
                traverseSnapshot = grantRuntimeSnapshotService.CaptureTraverseSnapshot(accountSid, traversePath);

            if (AclHelper.IsContainerSid(accountSid))
            {
                containerGrantSnapshots = grantRuntimeSnapshotService.CaptureEntrySnapshotsForPath(
                    normalized,
                    isTraverseOnly: false);
                if (!string.IsNullOrEmpty(traversePath))
                {
                    containerTraverseSnapshots = grantRuntimeSnapshotService.CaptureEntrySnapshotsForPath(
                        traversePath,
                        isTraverseOnly: true);
                }
            }
            else if (!AclHelper.IsLowIntegritySid(accountSid))
            {
                lowIntegritySnapshot = grantRuntimeSnapshotService.CaptureGrantSnapshot(
                    AclHelper.LowIntegritySid,
                    normalized,
                    isDeny: false);
            }
        }

        try
        {
            grantRuntimeMutationService.ApplyTrackedGrantAcl(
                accountSid,
                normalized,
                isDeny,
                entry.SavedRights ?? SavedRightsState.DefaultForMode(isDeny),
                ResolveOwnerSid(accountSid, isDeny, entry.SavedRights));

            if (!isDeny)
            {
                if (traverseSnapshot != null)
                    grantRuntimeSnapshotService.RestoreTraverseSnapshot(traverseSnapshot);

                if (AclHelper.IsContainerSid(accountSid))
                {
                    grantRuntimeSnapshotService.RestoreLinkedEntrySnapshots(
                        normalized,
                        isTraverseOnly: false,
                        accountSid,
                        containerGrantSnapshots);
                    if (!string.IsNullOrEmpty(traversePath))
                    {
                        grantRuntimeSnapshotService.RestoreLinkedEntrySnapshots(
                            traversePath,
                            isTraverseOnly: true,
                            accountSid,
                            containerTraverseSnapshots);
                    }
                }
                else if (!AclHelper.IsLowIntegritySid(accountSid) && lowIntegritySnapshot != null)
                {
                    grantRuntimeSnapshotService.RestoreGrantSnapshot(lowIntegritySnapshot);
                }
            }
        }
        catch (Exception ex)
        {
            throw new GrantOperationException(
                GrantApplyFailureStep.FixGrantAclApply,
                normalized,
                grantIntentStoreSaveService.GetPrimaryConfigPath(existingLocation.Select(location => location.Store)),
                ex);
        }

        return new GrantApplyResult(GrantApplied: true);
    }

    private GrantApplyResult PersistGrantChange(
        string accountSid,
        string path,
        bool newIsDeny,
        SavedRightsState requestedRights,
        Func<bool>? confirm,
        IGrantIntentStore? store,
        bool allowOppositeModeSwitch,
        bool forceRuntimeApply,
        bool preferExistingAddSemantics)
    {
        if (confirm != null && !confirm())
            throw new OperationCanceledException("User declined the grant operation.");

        var normalized = Path.GetFullPath(path);
        var newRights = AclHelper.ClearBlockedGrantOwner(accountSid, requestedRights)!;
        var newEntry = new GrantedPathEntry
        {
            Path = normalized,
            IsDeny = newIsDeny,
            SavedRights = newRights
        };

        var allLocations = GetGrantLocationsForPath(accountSid, normalized);
        var sameModeLocations = allLocations
            .Where(location => location.Entry.IsDeny == newIsDeny)
            .ToList();
        var oppositeModeLocations = allLocations
            .Where(location => location.Entry.IsDeny != newIsDeny)
            .ToList();

        if (!allowOppositeModeSwitch && oppositeModeLocations.Count > 0)
        {
            throw new InvalidOperationException(
                $"An opposite-mode grant for '{normalized}' already exists for SID '{accountSid}'. Remove the existing " +
                $"{(oppositeModeLocations[0].Entry.IsDeny ? "deny" : "allow")} grant first.");
        }

        var finalStores = grantIntentStoreMutationService.ResolveFinalStores(
            store,
            sameModeLocations,
            oppositeModeLocations,
            allowOppositeModeSwitch);
        var affectedStores = allLocations.Select(location => location.Store)
            .Concat(finalStores)
            .Distinct()
            .ToList();
        var snapshots = grantIntentMutationStateRestorer.CaptureStoreSnapshots(
            affectedStores.Select(store => (store, accountSid)),
            normalized,
            traversePath: null,
            includeDeny: true);
        var primaryConfigPath = grantIntentStoreSaveService.GetPrimaryConfigPath(affectedStores);

        var priorEntry = allowOppositeModeSwitch
            ? oppositeModeLocations.FirstOrDefault()?.Entry ?? sameModeLocations.FirstOrDefault()?.Entry
            : sameModeLocations.FirstOrDefault()?.Entry;
        if (priorEntry != null && priorEntry.IsDeny == newIsDeny)
        {
            newEntry = priorEntry.Clone();
            newEntry.IsDeny = newIsDeny;
            newEntry.Path = normalized;
            newEntry.IsTraverseOnly = false;
            newEntry.SavedRights = newRights;
        }

        newEntry = fileSystemOperations.PrepareGrantEntryForPersistence(
            accountSid,
            newEntry,
            priorEntry?.SavedRights,
            priorEntry?.PreviousSaclLabel);

        bool storeModified = grantIntentStoreMutationService.WouldMutateGrantStores(newEntry, allLocations, finalStores);
        bool targetAclChange = forceRuntimeApply || storeModified ||
            RequiresTargetAclChange(priorEntry, newEntry, allowOppositeModeSwitch, oppositeModeLocations.Count > 0);
        if (!storeModified && !targetAclChange)
            return default;

        return ExecuteGrantMutation(
            accountSid,
            normalized,
            priorEntry,
            newEntry,
            affectedStores,
            snapshots,
            primaryConfigPath,
            storeModified,
            targetAclChange,
            preferExistingAddSemantics,
            () => grantIntentStoreMutationService.MutateGrantStores(accountSid, newEntry, allLocations, finalStores));
    }

    private GrantApplyResult ExecuteGrantMutation(
        string accountSid,
        string normalizedPath,
        GrantedPathEntry? priorEntry,
        GrantedPathEntry newEntry,
        IReadOnlyList<IGrantIntentStore> affectedStores,
        IReadOnlyList<GrantIntentStoreSnapshot> snapshots,
        string? primaryConfigPath,
        bool storeModified,
        bool targetAclChange,
        bool preferExistingAddSemantics,
        Action mutateStores)
    {
        if (!targetAclChange)
        {
            if (storeModified)
            {
                mutateStores();
                grantIntentStoreSaveService.Save(affectedStores, GrantApplyFailureStep.PostGrantMutationSave, normalizedPath);
            }

            return new GrantApplyResult(
                DatabaseModified: storeModified,
                DurableSaveCompleted: storeModified);
        }

        var mutationOrder = grantMutationOrderResolver.ForRightsChange(priorEntry, newEntry);

        string? ownerSid = ResolveOwnerSid(accountSid, newEntry.IsDeny, newEntry.SavedRights);
        bool resetOwnerOnRemoval = mutationOrder == GrantMutationOrder.RemoveSaveAdd &&
            priorEntry != null &&
            !priorEntry.IsDeny &&
            priorEntry.SavedRights?.Own == true &&
            (newEntry.IsDeny || newEntry.SavedRights?.Own != true);
        bool resetOwnerAfterApply = ShouldResetOwner(priorEntry, newEntry) && !resetOwnerOnRemoval;
        var ownerRollbackState = grantAclRollbackService.CaptureOwnerRollbackState(
            normalizedPath,
            ownerSid,
            resetOwnerOnRemoval || resetOwnerAfterApply);
        AdditiveGrantCompensationService.AdditiveGrantCompensationContext? additiveCompensation =
            mutationOrder == GrantMutationOrder.SaveThenApply
            ? additiveGrantCompensationService.Create(
                accountSid,
                newEntry.IsDeny,
                snapshots,
                normalizedPath,
                storeModified)
            : null;
        IReadOnlyList<GrantApplyWarning> warnings = [];
        bool durableSaveCompleted = storeModified;

        switch (mutationOrder)
        {
            case GrantMutationOrder.SaveThenApply:
                if (storeModified)
                {
                    mutateStores();
                    try
                    {
                        grantIntentStoreSaveService.Save(affectedStores, GrantApplyFailureStep.GrantIntentSave, normalizedPath);
                    }
                    catch (GrantOperationException ex)
                    {
                        grantIntentMutationStateRestorer.TryRestoreStoreSnapshots(snapshots, normalizedPath, ex);
                        throw;
                    }
                }

                ApplyGrantAclWithRollback(
                    accountSid,
                    normalizedPath,
                    priorEntry,
                    newEntry,
                    ownerSid,
                    resetOwnerAfterApply,
                    preferExistingAddSemantics,
                    ownerRollbackState,
                    primaryConfigPath,
                    storeModified,
                    snapshots,
                    additiveCompensation);
                break;

            case GrantMutationOrder.ApplyThenSave:
                ApplyGrantAclWithRollback(
                    accountSid,
                    normalizedPath,
                    priorEntry,
                    newEntry,
                    ownerSid,
                    resetOwnerAfterApply,
                    preferExistingAddSemantics,
                    ownerRollbackState,
                    primaryConfigPath,
                    storeModified: false,
                    snapshots,
                    additiveCompensation: null);

                if (storeModified)
                {
                    mutateStores();
                    warnings = grantIntentStoreSaveService.SaveWithWarnings(
                        affectedStores,
                        GrantApplyFailureStep.PostGrantMutationSave,
                        normalizedPath);
                    durableSaveCompleted = warnings.Count == 0;
                }
                break;

            case GrantMutationOrder.RemoveSaveAdd:
                if (priorEntry != null)
                {
                    try
                    {
                        RemoveRuntimeGrant(accountSid, normalizedPath, priorEntry, updateFileSystem: true);
                        if (resetOwnerOnRemoval)
                            fileSystemOperations.ResetOwner(normalizedPath, recursive: false);
                    }
                    catch (Exception ex)
                    {
                        var operationException = new GrantOperationException(
                            GrantApplyFailureStep.GrantAclApply,
                            normalizedPath,
                            primaryConfigPath,
                            ex);
                        grantAclRollbackService.TryRollbackGrantAcl(
                            accountSid,
                            normalizedPath,
                            priorEntry,
                            newEntry,
                            ownerRollbackState,
                            primaryConfigPath,
                            operationException,
                            removeNewEntryBeforeRestore: false);
                        throw operationException;
                    }
                }

                if (storeModified)
                {
                    mutateStores();
                    try
                    {
                        grantIntentStoreSaveService.Save(affectedStores, GrantApplyFailureStep.GrantIntentSave, normalizedPath);
                    }
                    catch (GrantOperationException ex)
                    {
                        grantAclRollbackService.TryRollbackGrantAcl(
                            accountSid,
                            normalizedPath,
                            priorEntry,
                            newEntry,
                            ownerRollbackState,
                            primaryConfigPath,
                            ex,
                            removeNewEntryBeforeRestore: false);
                        grantIntentMutationStateRestorer.TryRestoreStoreSnapshots(snapshots, normalizedPath, ex);
                        throw;
                    }
                }

                ApplyGrantAclWithRollback(
                    accountSid,
                    normalizedPath,
                    priorEntry,
                    newEntry,
                    ownerSid,
                    resetOwnerAfterApply,
                    preferExistingAddSemantics: false,
                    ownerRollbackState,
                    primaryConfigPath,
                    storeModified,
                    snapshots,
                    additiveCompensation: null,
                    applyFromRemovalState: true);
                break;

            default:
                throw new InvalidOperationException($"Unsupported grant mutation order '{mutationOrder}'.");
        }

        return new GrantApplyResult(
            GrantApplied: targetAclChange,
            DatabaseModified: storeModified,
            DurableSaveCompleted: durableSaveCompleted,
            Warnings: warnings);
    }

    private void ApplyGrantAclWithRollback(
        string accountSid,
        string normalizedPath,
        GrantedPathEntry? priorEntry,
        GrantedPathEntry newEntry,
        string? ownerSid,
        bool shouldResetOwner,
        bool preferExistingAddSemantics,
        GrantAclRollbackService.OwnerRollbackState ownerRollbackState,
        string? primaryConfigPath,
        bool storeModified,
        IReadOnlyList<GrantIntentStoreSnapshot> snapshots,
        AdditiveGrantCompensationService.AdditiveGrantCompensationContext? additiveCompensation,
        bool applyFromRemovalState = false)
    {
        try
        {
            grantRuntimeMutationService.ApplyRuntimeGrantChange(
                accountSid,
                applyFromRemovalState ? null : priorEntry,
                newEntry,
                ownerSid,
                shouldResetOwner,
                preferExistingAddSemantics);
        }
        catch (Exception ex)
        {
            var operationException = new GrantOperationException(
                GrantApplyFailureStep.GrantAclApply,
                normalizedPath,
                primaryConfigPath,
                ex);
            var filesystemRestoredFromSnapshot = false;
            if (additiveCompensation is { } compensation)
            {
                filesystemRestoredFromSnapshot = grantAclRollbackService.TryRestoreTargetSecuritySnapshot(
                    normalizedPath,
                    compensation.RestoreSnapshot.PreviousTargetSecurity,
                    primaryConfigPath,
                    operationException);
            }

            if (filesystemRestoredFromSnapshot)
            {
                if (additiveCompensation is { } compensationAfterSnapshotRestore)
                    additiveGrantCompensationService.RestoreSideEffectsAfterTargetSecurityRestore(
                        accountSid,
                        normalizedPath,
                        compensationAfterSnapshotRestore,
                        operationException);
            }
            else
            {
                grantAclRollbackService.TryRollbackGrantAcl(
                    accountSid,
                    normalizedPath,
                    priorEntry,
                    newEntry,
                    ownerRollbackState,
                    primaryConfigPath,
                    operationException,
                    removeNewEntryBeforeRestore: true);
            }

            if (additiveCompensation is { } compensationAfterRollback)
            {
                var intentRestored = additiveGrantCompensationService.TryRestoreSavedIntent(
                    accountSid,
                    normalizedPath,
                    compensationAfterRollback,
                    operationException);

                if (!intentRestored && storeModified)
                    grantIntentMutationStateRestorer.TryRestoreStoreSnapshots(snapshots, normalizedPath, operationException);
            }
            else if (storeModified)
            {
                grantIntentMutationStateRestorer.TryRestoreStoreSnapshots(snapshots, normalizedPath, operationException);
            }

            throw operationException;
        }
    }

    private static string? ResolveOwnerSid(string accountSid, bool isDeny, SavedRightsState? savedRights)
    {
        if (isDeny || savedRights?.Own != true || !AclHelper.CanAssignGrantOwner(accountSid))
            return null;

        return accountSid;
    }

    private static bool ShouldResetOwner(GrantedPathEntry? previousEntry, GrantedPathEntry newEntry)
    {
        if (newEntry.SavedRights?.Own == true && newEntry.IsDeny)
            return true;

        if (previousEntry == null)
            return false;

        var previousOwn = previousEntry.SavedRights?.Own == true;
        if (!previousEntry.IsDeny && previousOwn && !newEntry.IsDeny && newEntry.SavedRights?.Own != true)
            return true;

        return previousEntry.IsDeny != newEntry.IsDeny && previousOwn && !previousEntry.IsDeny;
    }

    private List<GrantIntentLocation> GetGrantLocationsForPath(string sid, string normalizedPath)
        => grantIntentStoreMutationService.GetGrantLocationsForPath(sid, normalizedPath);

    private List<GrantIntentLocation> GetAllGrantLocations(string sid)
        => grantIntentStoreMutationService.GetAllGrantLocations(sid);

    private static IEnumerable<(string Path, bool IsDeny, GrantedPathEntry Entry, IReadOnlyList<GrantIntentLocation> Locations)>
        GroupGrantLocationsByPath(IEnumerable<GrantIntentLocation> locations)
        => locations
            .GroupBy(location => (location.Entry.Path, location.Entry.IsDeny))
            .Select(group => (group.Key.Path, group.Key.IsDeny, group.First().Entry, (IReadOnlyList<GrantIntentLocation>)group.ToList()));

    private bool RestoreGrantStoresToExactLocations(
        string sid,
        IReadOnlyList<GrantIntentLocation> currentLocations,
        IReadOnlyList<GrantIntentRestoreLocation> desiredLocations,
        bool mutate)
        => grantIntentStoreMutationService.RestoreGrantStoresToExactLocations(
            sid,
            currentLocations,
            desiredLocations,
            mutate);

    private bool RequiresTargetAclChange(
        GrantedPathEntry? priorEntry,
        GrantedPathEntry newEntry,
        bool allowOppositeModeSwitch,
        bool hasOppositeModeEntry)
    {
        if (allowOppositeModeSwitch && hasOppositeModeEntry)
            return true;

        if (priorEntry == null)
            return true;

        return !traverseGrantStateService.EntriesEquivalent(priorEntry, newEntry);
    }

    private void RemoveRuntimeGrant(string sid, string path, GrantedPathEntry entry, bool updateFileSystem)
        => grantRuntimeMutationService.RemoveRuntimeGrant(sid, path, entry, updateFileSystem);

    private void RemoveTrackedGrantWithoutFilesystem(
        string sid,
        string normalizedPath,
        GrantedPathEntry entry)
        => grantRuntimeMutationService.RemoveTrackedGrantWithoutFilesystem(
            sid,
            normalizedPath,
            entry);

    private void RemoveGrantEntries(string sid, string normalizedPath, IEnumerable<GrantIntentLocation> locations)
        => grantIntentStoreMutationService.RemoveGrantEntries(sid, normalizedPath, locations);
}
