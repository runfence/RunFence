using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Acl.Permissions;
using RunFence.Acl.Traverse;
using RunFence.Persistence;
using System.Security.Principal;

namespace RunFence.Acl;

/// <summary>
/// Facade over grant/traverse core operations, confirm workflows, low-level filesystem grant
/// operations, and sync helpers (interactive user + Low IL + filesystem-to-DB sync).
/// </summary>
public class PathGrantService(
    IGrantCoreOperations grantCore,
    ITraverseCoreOperations traverseCore,
    UiThreadDatabaseAccessor dbAccessor,
    ContainerInteractiveUserSync containerIuSync,
    LowIntegrityGrantSync lowIntegrityGrantSync,
    IGrantSyncService syncService,
    IMandatoryLabelService mandatoryLabelService,
    GrantFileSystemOperations fileSystemOperations,
    GrantAccessEnsurer accessEnsurer,
    IGrantAceService grantAceService,
    IFileSystemPathInfo pathInfo,
    IAclAccessor aclAccessor,
    ITraverseGrantOwnerResolver traverseGrantOwnerResolver,
    ITraverseIntentStoreCoordinator traverseIntentStoreCoordinator,
    TraverseGrantStateService traverseGrantStateService,
    Func<IGrantIntentStoreProvider> grantIntentStoreProvider,
    Func<IGrantIntentRepository> grantIntentRepository,
    Func<IGrantIntentStore> mainGrantIntentStore,
    IGrantIntentStoreSaveService grantIntentStoreSaveService,
    TraverseRestoreWorkflow traverseRestoreWorkflow) : IPathGrantService
{
    private readonly record struct GrantIntentSnapshot(
        IGrantIntentStore Store,
        IReadOnlyList<GrantedPathEntry> Entries);
    private enum GrantMutationOrder
    {
        SaveThenApply,
        ApplyThenSave,
        RemoveSaveAdd
    }
    private readonly record struct OwnerRollbackState(bool OwnerMayHaveChanged, string? OriginalOwnerSid);
    private readonly record struct RuntimeEntrySnapshot(
        string Sid,
        string Path,
        bool IsTraverseOnly,
        bool IsDeny,
        GrantedPathEntry? Entry);
    private IGrantIntentStoreProvider GrantIntentStoreProvider => grantIntentStoreProvider();
    private IGrantIntentRepository GrantIntentRepository => grantIntentRepository();
    private IGrantIntentStore MainGrantIntentStore => mainGrantIntentStore();

    // --- Grants ---

    private GrantOperationResult ApplyTrackedGrantAcl(string sid, string path, bool isDeny,
        SavedRightsState savedRights, string? ownerSid = null)
        => ApplyGrantCore(sid, path, isDeny, savedRights, ownerSid, skipAllowSideEffectsWhenGrantAlreadyTracked: false);

    private GrantOperationResult AddGrantWithoutPersisting(string sid, string path, bool isDeny,
        SavedRightsState? savedRights = null, string? ownerSid = null)
        => ApplyGrantCore(
            sid,
            path,
            isDeny,
            savedRights ?? SavedRightsState.DefaultForMode(isDeny),
            ownerSid,
            skipAllowSideEffectsWhenGrantAlreadyTracked: true);

    public GrantApplyResult AddGrant(string accountSid, string path, bool isDeny,
        SavedRightsState? savedRights, Func<bool>? confirm, IGrantIntentStore? store = null)
        => PersistGrantChange(
            accountSid,
            path,
            newIsDeny: isDeny,
            savedRights ?? SavedRightsState.DefaultForMode(isDeny),
            confirm,
            store,
            allowOppositeModeSwitch: false,
            forceRuntimeApply: true,
            preferExistingAddSemantics: true);

    private GrantOperationResult ApplyGrantCore(string sid, string path, bool isDeny,
        SavedRightsState rights, string? ownerSid, bool skipAllowSideEffectsWhenGrantAlreadyTracked)
    {
        var normalized = Path.GetFullPath(path);
        var operationResult = fileSystemOperations.AddGrant(sid, normalized, isDeny, rights, ownerSid);

        if (isDeny || (skipAllowSideEffectsWhenGrantAlreadyTracked && !operationResult.GrantAdded))
            return operationResult;

        bool traverseAdded = false;
        var traverseDir = pathInfo.DirectoryExists(normalized) ? normalized : Path.GetDirectoryName(normalized);
        if (!string.IsNullOrEmpty(traverseDir))
        {
            var (modified, _) = traverseCore.AddTraverse(sid, traverseDir);
            traverseAdded = modified;
        }

        if (AclHelper.IsContainerSid(sid))
        {
            var iuResult = containerIuSync.SyncAllowGrantToInteractiveUser(sid, normalized, rights);
            traverseAdded |= iuResult.TraverseAdded;
        }

        if (AclHelper.IsLowIntegritySid(sid))
        {
            var sources = dbAccessor.Read(db =>
                db.Accounts
                    .Where(a => !AclHelper.IsLowIntegritySid(a.Sid) && !AclHelper.IsContainerSid(a.Sid))
                    .Where(a => a.Grants.Any(g => !g.IsTraverseOnly && !g.IsDeny &&
                        string.Equals(g.Path, normalized, StringComparison.OrdinalIgnoreCase)))
                    .Select(a => a.Sid)
                    .ToList());
            if (sources.Count > 0)
            {
                dbAccessor.Write(db =>
                {
                    var entry = GrantCoreOperations.FindGrantEntryInDb(db, sid, normalized, isDeny: false);
                    if (entry != null)
                        entry.SourceSids = sources;
                });
            }
        }

        if (!AclHelper.IsLowIntegritySid(sid) && !AclHelper.IsContainerSid(sid))
        {
            dbAccessor.Write(db =>
            {
                var lowIlEntry = GrantCoreOperations.FindGrantEntryInDb(
                    db, AclHelper.LowIntegritySid, normalized, isDeny: false);
                if (lowIlEntry?.SourceSids != null &&
                    !lowIlEntry.SourceSids.Contains(sid, StringComparer.OrdinalIgnoreCase))
                    lowIlEntry.SourceSids.Add(sid);
            });
        }

        return new GrantOperationResult(
            GrantAdded: operationResult.GrantAdded,
            TraverseAdded: traverseAdded,
            DatabaseModified: operationResult.DatabaseModified || traverseAdded);
    }

    private bool RemoveGrantAclOnly(string sid, string path, bool isDeny,
        SavedRightsState? savedRights = null, string? previousSaclLabel = null)
    {
        var normalized = Path.GetFullPath(path);
        grantAceService.RevertAce(normalized, sid, isDeny);

        if (isDeny)
            return true;

        traverseCore.CleanupOrphanedTraverse(sid, normalized);

        if (AclHelper.IsContainerSid(sid))
            containerIuSync.RevertInteractiveUserGrant(sid, normalized);
        else if (AclHelper.IsLowIntegritySid(sid))
        {
            if (savedRights?.Write == true)
                mandatoryLabelService.RestoreMandatoryLabel(normalized, previousSaclLabel);
        }
        else
            lowIntegrityGrantSync.RevertSource(sid, normalized, updateFileSystem: true);

        return true;
    }

    public GrantApplyResult EnsureAccess(string sid, string path, SavedRightsState savedRights,
        Func<string, string, bool>? confirm = null, bool unelevated = false)
        => accessEnsurer.EnsureAccess(sid, path, savedRights, confirm, unelevated);

    public GrantApplyResult EnsureAccess(string sid, string path, System.Security.AccessControl.FileSystemRights rights,
        Func<string, string, bool>? confirm = null, bool unelevated = false)
        => accessEnsurer.EnsureAccess(sid, path, rights, confirm, unelevated);

    public GrantApplyResult EnsureTemporaryAccess(string sid, string path, SavedRightsState savedRights,
        Func<string, string, bool>? confirm = null, bool unelevated = false)
        => accessEnsurer.EnsureTemporaryAccess(sid, path, savedRights, confirm, unelevated);

    public GrantApplyResult EnsureTemporaryAccess(string sid, string path, System.Security.AccessControl.FileSystemRights rights,
        Func<string, string, bool>? confirm = null, bool unelevated = false)
        => accessEnsurer.EnsureTemporaryAccess(sid, path, rights, confirm, unelevated);

    private bool RemoveGrantWithoutPersisting(string sid, string path, bool isDeny, bool updateFileSystem)
    {
        var normalized = Path.GetFullPath(path);
        bool removed = fileSystemOperations.RemoveGrant(sid, normalized, isDeny, updateFileSystem);
        if (!removed || isDeny)
            return removed;

        traverseCore.CleanupOrphanedTraverse(sid, normalized);

        if (AclHelper.IsContainerSid(sid))
            containerIuSync.RevertInteractiveUserGrant(sid, normalized);
        else if (!AclHelper.IsLowIntegritySid(sid))
            lowIntegrityGrantSync.RevertSource(sid, normalized, updateFileSystem);

        return true;
    }

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
            if (!RemoveGrantWithoutPersisting(accountSid, normalized, isDeny, updateFileSystem: true))
            {
                RemoveGrantAclOnly(
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

    public GrantApplyResult RestoreGrant(string accountSid, string path, bool isDeny,
        GrantIntentRestoreSnapshot previousState)
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
            .Select(location => GrantIntentStoreProvider.ResolveStore(location.ConfigPath))
            .Distinct()
            .ToList();
        var affectedStores = allLocations.Select(location => location.Store)
            .Concat(finalStores)
            .Distinct()
            .ToList();
        var snapshots = CaptureGrantSnapshots(accountSid, normalized, affectedStores);
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

    private GrantOperationResult UpdateGrantWithoutPersisting(string sid, string path, bool isDeny,
        SavedRightsState savedRights, string? ownerSid = null)
        => fileSystemOperations.UpdateGrant(sid, path, isDeny, savedRights, ownerSid);

    public GrantApplyResult UpdateGrant(string accountSid, string path, bool isDeny,
        SavedRightsState savedRights, Func<bool>? confirm, IGrantIntentStore? store = null)
        => PersistGrantChange(
            accountSid,
            path,
            newIsDeny: isDeny,
            savedRights,
            confirm,
            store,
            allowOppositeModeSwitch: false,
            forceRuntimeApply: true,
            preferExistingAddSemantics: false);

    public GrantApplyResult SwitchGrantMode(string accountSid, string path, bool newIsDeny,
        SavedRightsState savedRights, Func<bool>? confirm, IGrantIntentStore? store = null)
        => PersistGrantChange(
            accountSid,
            path,
            newIsDeny,
            savedRights,
            confirm,
            store,
            allowOppositeModeSwitch: true,
            forceRuntimeApply: true,
            preferExistingAddSemantics: false);

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
            existingLocations[0].Entry,
            cleanupDerivedSources: true);
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

    public GrantApplyResult RemoveAll(string accountSid)
    {
        var grantLocations = GetAllGrantLocations(accountSid);
        var traverseLocations = traverseIntentStoreCoordinator.GetAllTraverseLocations(accountSid);
        if (grantLocations.Count == 0 && traverseLocations.Count == 0)
            return default;

        var affectedStores = grantLocations.Select(location => location.Store)
            .Concat(traverseLocations.Select(location => location.Store))
            .Distinct()
            .ToList();
        var primaryConfigPath = grantIntentStoreSaveService.GetPrimaryConfigPath(affectedStores);
        var trackedTraversePaths = traverseLocations
            .Select(location => location.Entry.Path)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var remainingTraverseEntries = traverseGrantStateService.GetRemainingTraverseEntriesForCleanup(accountSid, traverseLocations);
        var grantPaths = traverseGrantStateService.GetTraverseGrantPathsForCleanup(accountSid, grantLocations);

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

        foreach (var traverseGroup in GroupTraverseLocationsByPath(traverseLocations))
        {
            var removingPaths = traverseGrantStateService.CollectStoredTraversePaths(traverseGroup.Locations.Select(location => location.Entry));

            try
            {
                traverseCore.RemoveTraverseAces(
                    accountSid,
                    removingPaths
                        .Where(pathToRemove =>
                        {
                            if (grantPaths.Contains(pathToRemove))
                                return false;

                            return !remainingTraverseEntries.Any(entry => traverseGrantStateService.CollectStoredTraversePaths(entry)
                                .Contains(pathToRemove, StringComparer.OrdinalIgnoreCase));
                        })
                        .ToList());
            }
            catch (Exception ex)
            {
                throw new GrantOperationException(
                    GrantApplyFailureStep.RemoveAllTraverseAclRemove,
                    traverseGroup.Path,
                    primaryConfigPath,
                    ex);
            }
        }

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
                    !trackedTraversePaths.Contains(traversePath))
                {
                    traverseCore.CleanupOrphanedTraverse(accountSid, traversePath);
                }
            }

            RemoveGrantEntries(accountSid, grantGroup.Path, grantGroup.Locations);
        }

        foreach (var traverseGroup in GroupTraverseLocationsByPath(traverseLocations))
        {
            RemoveTrackedTraverseWithoutFilesystem(accountSid, traverseGroup.Path);
            RemoveTraverseEntriesFromStores(accountSid, traverseGroup.Path, traverseGroup.Locations);
        }

        var warnings = grantIntentStoreSaveService.SaveWithWarnings(
            affectedStores,
            GrantApplyFailureStep.PostRemoveAllSave,
            GetRepresentativePath(grantLocations, traverseLocations));

        return new GrantApplyResult(
            GrantApplied: grantLocations.Count > 0,
            TraverseApplied: traverseLocations.Count > 0,
            DatabaseModified: true,
            DurableSaveCompleted: warnings.Count == 0,
            Warnings: warnings);
    }

    public GrantApplyResult UntrackAll(string accountSid)
    {
        var grantLocations = GetAllGrantLocations(accountSid);
        var traverseLocations = traverseIntentStoreCoordinator.GetAllTraverseLocations(accountSid);
        if (grantLocations.Count == 0 && traverseLocations.Count == 0)
            return default;

        foreach (var grantGroup in GroupGrantLocationsByPath(grantLocations))
        {
            RemoveTrackedGrantWithoutFilesystem(
                accountSid,
                grantGroup.Path,
                grantGroup.Entry,
                cleanupDerivedSources: true);
            RemoveGrantEntries(accountSid, grantGroup.Path, grantGroup.Locations);
        }

        foreach (var traverseGroup in GroupTraverseLocationsByPath(traverseLocations))
        {
            RemoveTrackedTraverseWithoutFilesystem(accountSid, traverseGroup.Path);
            RemoveTraverseEntriesFromStores(accountSid, traverseGroup.Path, traverseGroup.Locations);
        }

        var warnings = grantIntentStoreSaveService.SaveWithWarnings(
            grantLocations.Select(location => location.Store)
                .Concat(traverseLocations.Select(location => location.Store)),
            GrantApplyFailureStep.UntrackAllSave,
            GetRepresentativePath(grantLocations, traverseLocations));

        return new GrantApplyResult(
            DatabaseModified: true,
            DurableSaveCompleted: warnings.Count == 0,
            Warnings: warnings);
    }

    public bool UpdateFromPath(string path, string? sid = null)
        => syncService.UpdateFromPath(path, sid);

    public GrantIntentRestoreSnapshot CaptureGrantRestoreSnapshot(string sid, string path, bool isDeny)
    {
        var normalized = Path.GetFullPath(path);
        var locations = GetGrantLocationsForPath(sid, normalized)
            .Where(location => location.Entry.IsDeny == isDeny)
            .Select(location => new GrantIntentRestoreLocation(location.Store.ConfigPath, location.Entry))
            .ToList();
        var runtimeEntry = dbAccessor.Read(db =>
            GrantCoreOperations.FindGrantEntryInDb(db, sid, normalized, isDeny)?.Clone());
        return new GrantIntentRestoreSnapshot(runtimeEntry, locations);
    }

    public GrantIntentRestoreSnapshot CaptureTraverseRestoreSnapshot(string sid, string path)
    {
        var normalized = Path.GetFullPath(path);
        var locations = traverseIntentStoreCoordinator.GetTraverseLocationsForPath(
                sid,
                normalized,
                includeManualSharedEntries: true)
            .Select(location => new GrantIntentRestoreLocation(location.Store.ConfigPath, location.Entry))
            .ToList();
        var runtimeEntry = dbAccessor.Read(db =>
            FindRuntimeTraverseEntry(db, sid, normalized, includeManualSharedEntries: true)?.Clone());
        return new GrantIntentRestoreSnapshot(runtimeEntry, locations);
    }

    // --- Traverse ---

    public GrantApplyResult AddTraverse(string accountSid, string path, IGrantIntentStore? store = null)
    {
        var normalized = Path.GetFullPath(path);
        var coveragePaths = traverseCore.CollectCoveragePaths(normalized);
        var pathsNeedingAce = traverseCore.GetPathsNeedingTraverseAce(accountSid, coveragePaths);
        var existingLocations = traverseIntentStoreCoordinator.GetTraverseLocationsForPath(
            accountSid,
            normalized,
            includeManualSharedEntries: true);
        var finalStores = store != null
            ? [store]
            : existingLocations.Count > 0
                ? existingLocations.Select(location => location.Store).Distinct().ToList()
                : [MainGrantIntentStore];
        var affectedStores = existingLocations.Select(location => location.Store)
            .Concat(finalStores)
            .Distinct()
            .ToList();
        var storeOwnerSid = traverseIntentStoreCoordinator.ResolveStorageOwnerSid(accountSid);
        var snapshots = traverseGrantStateService.CaptureStoreSnapshots(storeOwnerSid, normalized, affectedStores);
        var runtimeSnapshot = dbAccessor.Read(db =>
            FindRuntimeTraverseEntry(db, accountSid, normalized, includeManualSharedEntries: true)?.Clone());
        var primaryConfigPath = grantIntentStoreSaveService.GetPrimaryConfigPath(affectedStores);
        bool storeModified = false;
        var finalStoreSet = finalStores.ToHashSet();

        foreach (var location in existingLocations)
        {
            if (finalStoreSet.Contains(location.Store))
                continue;

            storeModified |= traverseIntentStoreCoordinator.RemoveTraverseEntryFromStore(
                accountSid,
                location.Store,
                location.Entry);
        }

        foreach (var targetStore in finalStores)
        {
            var currentEntry = existingLocations.FirstOrDefault(location =>
                ReferenceEquals(location.Store, targetStore))?.Entry;
            var newEntry = traverseIntentStoreCoordinator.BuildTraverseEntry(
                accountSid,
                normalized,
                coveragePaths,
                currentEntry);
            if (currentEntry == null)
            {
                targetStore.AddEntry(storeOwnerSid, newEntry);
                storeModified = true;
                continue;
            }

            if (!traverseGrantStateService.EntriesEquivalent(currentEntry, newEntry))
            {
                targetStore.ReplaceEntry(storeOwnerSid, currentEntry, newEntry);
                storeModified = true;
            }
        }

        var traverseEntry = traverseIntentStoreCoordinator.BuildTraverseEntry(
            accountSid,
            normalized,
            coveragePaths,
            runtimeSnapshot);
        traverseCore.TrackTraverse(accountSid, traverseEntry);

        if (storeModified)
        {
            try
            {
                grantIntentStoreSaveService.Save(affectedStores, GrantApplyFailureStep.TraverseIntentSave, normalized);
            }
            catch (GrantOperationException ex)
            {
                TryRestoreTrackedRuntimeTraverseEntry(accountSid, normalized, runtimeSnapshot, ex);
                TryRestoreTraverseSnapshots(storeOwnerSid, normalized, snapshots, ex);
                throw;
            }
        }

        List<string> appliedPaths = [];
        try
        {
            if (pathsNeedingAce.Count > 0)
                appliedPaths = traverseCore.ApplyTraverseAces(accountSid, pathsNeedingAce).ToList();
        }
        catch (Exception ex)
        {
            var operationException = new GrantOperationException(
                GrantApplyFailureStep.TraverseAclApply,
                normalized,
                primaryConfigPath,
                ex is TraverseAclApplyException applyEx ? applyEx.InnerException ?? applyEx : ex);
            var rollbackPaths = ex is TraverseAclApplyException applyFailure
                ? applyFailure.AppliedPaths
                : appliedPaths;
            TryRollbackTraverseAcl(accountSid, rollbackPaths, primaryConfigPath, operationException);
            TryRestoreTrackedRuntimeTraverseEntry(accountSid, normalized, runtimeSnapshot, operationException);
            if (storeModified)
                TryRestoreTraverseSnapshots(storeOwnerSid, normalized, snapshots, operationException);
            throw operationException;
        }

        try
        {
            if (appliedPaths.Count > 0)
                traverseCore.VerifyEffectiveTraverse(accountSid, coveragePaths);

            if (AclHelper.IsContainerSid(accountSid))
                containerIuSync.SyncTraverseToInteractiveUser(accountSid, normalized);
        }
        catch (Exception ex)
        {
            var operationException = new GrantOperationException(
                GrantApplyFailureStep.TraverseEffectiveAccessValidation,
                normalized,
                primaryConfigPath,
                ex);
            TryRollbackTraverseAcl(accountSid, appliedPaths, primaryConfigPath, operationException);
            TryRestoreTrackedRuntimeTraverseEntry(accountSid, normalized, runtimeSnapshot, operationException);
            if (storeModified)
                TryRestoreTraverseSnapshots(storeOwnerSid, normalized, snapshots, operationException);
            throw operationException;
        }

        return new GrantApplyResult(
            TraverseApplied: appliedPaths.Count > 0,
            DatabaseModified: storeModified,
            DurableSaveCompleted: storeModified);
    }

    private (bool Modified, List<string> VisitedPaths) AddTraverseWithoutPersisting(string sid, string path)
    {
        var (modified, visitedPaths) = traverseCore.AddTraverse(sid, path);

        bool iuModified = false;
        if (AclHelper.IsContainerSid(sid))
            iuModified = containerIuSync.SyncTraverseToInteractiveUser(sid, Path.GetFullPath(path));

        return (modified || iuModified, visitedPaths);
    }

    public GrantApplyResult RemoveTraverse(string accountSid, string path)
    {
        var normalized = Path.GetFullPath(path);
        var existingLocations = traverseIntentStoreCoordinator.GetTraverseLocationsForPath(
            accountSid,
            normalized,
            includeManualSharedEntries: false);
        if (existingLocations.Count == 0)
            return default;

        var primaryConfigPath = grantIntentStoreSaveService.GetPrimaryConfigPath(existingLocations.Select(location => location.Store));
        var removingPaths = traverseGrantStateService.CollectStoredTraversePaths(existingLocations.Select(location => location.Entry));
        var remainingEntries = traverseGrantStateService.GetRemainingTraverseEntriesForCleanup(accountSid, existingLocations);
        var grantPaths = traverseGrantStateService.GetTraverseGrantPathsForCleanup(accountSid, []);

        try
        {
            traverseCore.RemoveTraverseAces(
                accountSid,
                removingPaths
                    .Where(pathToRemove =>
                    {
                        if (grantPaths.Contains(pathToRemove))
                            return false;

                        return !remainingEntries.Any(entry => traverseGrantStateService.CollectStoredTraversePaths(entry)
                            .Contains(pathToRemove, StringComparer.OrdinalIgnoreCase));
                    })
                    .ToList());
        }
        catch (Exception ex)
        {
            throw new GrantOperationException(
                GrantApplyFailureStep.TraverseAclRemove,
                normalized,
                primaryConfigPath,
                ex);
        }

        RemoveTrackedTraverseWithoutFilesystem(accountSid, normalized);
        RemoveTraverseEntriesFromStores(accountSid, normalized, existingLocations);
        var warnings = grantIntentStoreSaveService.SaveWithWarnings(
            existingLocations.Select(location => location.Store),
            GrantApplyFailureStep.PostTraverseRemoveSave,
            normalized);

        return new GrantApplyResult(
            TraverseApplied: true,
            DatabaseModified: true,
            DurableSaveCompleted: warnings.Count == 0,
            Warnings: warnings);
    }

    public GrantApplyResult RestoreTraverse(string accountSid, string path,
        GrantIntentRestoreSnapshot previousState)
        => traverseRestoreWorkflow.Restore(accountSid, Path.GetFullPath(path), previousState);

    private bool RemoveTraverseWithoutPersisting(string sid, string path, bool updateFileSystem)
    {
        bool removed = traverseCore.RemoveTraverse(sid, path, updateFileSystem);

        if (removed && AclHelper.IsContainerSid(sid))
            containerIuSync.RevertInteractiveUserTraverse(sid, Path.GetFullPath(path));

        return removed;
    }

    public GrantApplyResult UntrackTraverse(string accountSid, string path)
    {
        var normalized = Path.GetFullPath(path);
        var existingLocations = traverseIntentStoreCoordinator.GetTraverseLocationsForPath(
            accountSid,
            normalized,
            includeManualSharedEntries: false);
        if (existingLocations.Count == 0)
            return default;

        RemoveTrackedTraverseWithoutFilesystem(accountSid, normalized);
        RemoveTraverseEntriesFromStores(accountSid, normalized, existingLocations);
        var warnings = grantIntentStoreSaveService.SaveWithWarnings(
            existingLocations.Select(location => location.Store),
            GrantApplyFailureStep.UntrackTraverseSave,
            normalized);

        return new GrantApplyResult(
            DatabaseModified: true,
            DurableSaveCompleted: warnings.Count == 0,
            Warnings: warnings);
    }

    public void CleanupOrphanedTraverse(string sid, string path)
        => traverseCore.CleanupOrphanedTraverse(sid, Path.GetFullPath(path));

    public List<string> FixTraverse(string sid, string path)
        => traverseCore.FixTraverse(sid, path);

    public GrantApplyResult FixTraverseAcl(string accountSid, string path)
    {
        var normalized = Path.GetFullPath(path);
        var existingLocation = traverseIntentStoreCoordinator.GetTraverseLocationsForPath(
            accountSid,
            normalized,
            includeManualSharedEntries: false)
            .ToList();
        if (existingLocation.Count == 0)
            return default;

        var storedPaths = traverseGrantStateService.CollectStoredTraversePaths(existingLocation.Select(location => location.Entry));
        var pathsNeedingAce = traverseCore.GetPathsNeedingTraverseAce(accountSid, storedPaths);
        if (pathsNeedingAce.Count == 0)
            return default;

        List<string> appliedPaths = [];
        try
        {
            appliedPaths = traverseCore.ApplyTraverseAces(accountSid, pathsNeedingAce).ToList();
            traverseCore.VerifyEffectiveTraverse(accountSid, storedPaths);
        }
        catch (Exception ex)
        {
            var operationException = new GrantOperationException(
                GrantApplyFailureStep.FixTraverseAclApply,
                normalized,
                grantIntentStoreSaveService.GetPrimaryConfigPath(existingLocation.Select(location => location.Store)),
                ex is TraverseAclApplyException applyEx ? applyEx.InnerException ?? applyEx : ex);
            var rollbackPaths = ex is TraverseAclApplyException applyFailure
                ? applyFailure.AppliedPaths
                : appliedPaths;
            TryRollbackTraverseAcl(
                accountSid,
                rollbackPaths,
                grantIntentStoreSaveService.GetPrimaryConfigPath(existingLocation.Select(location => location.Store)),
                operationException);
            throw operationException;
        }

        return new GrantApplyResult(
            TraverseApplied: appliedPaths.Count > 0);
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
        RuntimeEntrySnapshot traverseSnapshot = default;
        RuntimeEntrySnapshot lowIntegritySnapshot = default;
        IReadOnlyList<RuntimeEntrySnapshot> containerGrantSnapshots = [];
        IReadOnlyList<RuntimeEntrySnapshot> containerTraverseSnapshots = [];
        if (!isDeny)
        {
            traversePath = pathInfo.DirectoryExists(normalized) ? normalized : Path.GetDirectoryName(normalized);
            if (!string.IsNullOrEmpty(traversePath))
                traverseSnapshot = CaptureRuntimeTraverseSnapshot(accountSid, traversePath);

            if (AclHelper.IsContainerSid(accountSid))
            {
                containerGrantSnapshots = CaptureLinkedRuntimeEntrySnapshots(
                    normalized,
                    isTraverseOnly: false,
                    accountSid);
                if (!string.IsNullOrEmpty(traversePath))
                {
                    containerTraverseSnapshots = CaptureLinkedRuntimeEntrySnapshots(
                        traversePath,
                        isTraverseOnly: true,
                        accountSid);
                }
            }
            else if (!AclHelper.IsLowIntegritySid(accountSid))
            {
                lowIntegritySnapshot = CaptureRuntimeGrantSnapshot(
                    AclHelper.LowIntegritySid,
                    normalized,
                    isDeny: false);
            }
        }

        try
        {
            ApplyTrackedGrantAcl(
                accountSid,
                normalized,
                isDeny,
                entry.SavedRights ?? SavedRightsState.DefaultForMode(isDeny),
                ResolveOwnerSid(accountSid, isDeny, entry.SavedRights));

            if (!isDeny)
            {
                RestoreRuntimeTraverseSnapshot(traverseSnapshot);

                if (AclHelper.IsContainerSid(accountSid))
                {
                    RestoreLinkedRuntimeEntrySnapshots(
                        normalized,
                        isTraverseOnly: false,
                        accountSid,
                        containerGrantSnapshots);
                    if (!string.IsNullOrEmpty(traversePath))
                    {
                        RestoreLinkedRuntimeEntrySnapshots(
                            traversePath,
                            isTraverseOnly: true,
                            accountSid,
                            containerTraverseSnapshots);
                    }
                }
                else if (!AclHelper.IsLowIntegritySid(accountSid))
                {
                    RestoreRuntimeGrantSnapshot(lowIntegritySnapshot);
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

        return new GrantApplyResult(
            GrantApplied: true);
    }

    // --- Bulk / Query ---

    private void RemoveAllWithoutPersisting(string sid, bool updateFileSystem)
    {
        var allGrants = dbAccessor.Read(db =>
        {
            var account = db.GetAccount(sid);
            return account?.Grants.Select(e => e.Clone()).ToList() ?? [];
        });

        var trackedSharedTraversePaths = traverseIntentStoreCoordinator.UsesSharedContainerTraverse(sid)
            ? dbAccessor.Read(db =>
                traverseIntentStoreCoordinator.GetTraverseStoreOrEmpty(db, sid)
                    ?.Where(e => e.IsTraverseOnly &&
                                 e.SourceSids?.Contains(sid, StringComparer.OrdinalIgnoreCase) == true)
                    .Select(e => e.Path)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList()
                ?? [])
            : [];
        if (allGrants.Count == 0 && trackedSharedTraversePaths.Count == 0)
            return;

        var removedGrants = grantCore.RemoveAllGrants(sid, updateFileSystem);

        if (updateFileSystem)
        {
            if (AclHelper.IsSpecificContainerSid(sid))
            {
                foreach (var grant in removedGrants.Where(e => !e.IsDeny && !e.IsTraverseOnly))
                    traverseCore.CleanupOrphanedTraverse(sid, grant.Path);
            }
            else
            {
                traverseCore.RevertAllTraverseAces(sid, allGrants);
            }
        }

        if (AclHelper.IsSpecificContainerSid(sid))
        {
            foreach (var traversePath in trackedSharedTraversePaths)
            {
                traverseCore.RemoveTraverse(sid, traversePath, updateFileSystem);
                containerIuSync.RevertInteractiveUserTraverse(sid, traversePath);
            }
        }

        if (AclHelper.IsContainerSid(sid))
            containerIuSync.RevertAllInteractiveUserGrants(sid, removedGrants, updateFileSystem);
        else if (AclHelper.IsLowIntegritySid(sid))
        {
            if (updateFileSystem)
            {
                foreach (var grant in allGrants.Where(e => !e.IsDeny && e.SavedRights?.Write == true))
                    mandatoryLabelService.RestoreMandatoryLabel(grant.Path, grant.PreviousSaclLabel);
            }
        }
        else
            lowIntegrityGrantSync.RevertAllSources(sid, updateFileSystem);

        dbAccessor.Write(db => db.GetAccount(sid)?.Grants.Clear());
    }

    public void FixGrant(string sid, string path, bool isDeny)
        => fileSystemOperations.FixGrant(sid, path, isDeny);

    public GrantRightsState ReadGrantState(string path, string sid, IReadOnlyList<string> groupSids)
        => fileSystemOperations.ReadGrantState(path, sid, groupSids);

    public PathAclStatus CheckGrantStatus(string path, string sid, bool isDeny)
        => fileSystemOperations.CheckGrantStatus(path, sid, isDeny);

    public void ValidateGrant(string sid, string path, bool isDeny)
        => fileSystemOperations.ValidateGrant(sid, path, isDeny);

    // --- Ownership ---

    public void ChangeOwner(string path, string sid, bool recursive)
        => fileSystemOperations.ChangeOwner(path, sid, recursive);

    public void ResetOwner(string path, bool recursive)
        => fileSystemOperations.ResetOwner(path, recursive);

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
        if (newIsDeny && confirm == null)
            throw new InvalidOperationException("Deny grant operations require confirmation.");

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

        var finalStores = ResolveFinalStores(store, sameModeLocations, oppositeModeLocations, allowOppositeModeSwitch);
        var affectedStores = allLocations.Select(location => location.Store)
            .Concat(finalStores)
            .Distinct()
            .ToList();
        var snapshots = CaptureGrantSnapshots(accountSid, normalized, affectedStores);
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

        bool storeModified = WouldMutateGrantStores(newEntry, allLocations, finalStores);
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
            () => MutateGrantStores(accountSid, normalized, newEntry, allLocations, finalStores));
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

    private GrantApplyResult ExecuteGrantMutation(
        string accountSid,
        string normalizedPath,
        GrantedPathEntry? priorEntry,
        GrantedPathEntry newEntry,
        IReadOnlyList<IGrantIntentStore> affectedStores,
        IReadOnlyList<GrantIntentSnapshot> snapshots,
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

        GrantMutationOrder mutationOrder;
        if (priorEntry == null)
        {
            mutationOrder = GrantMutationOrder.SaveThenApply;
        }
        else if (priorEntry.IsDeny != newEntry.IsDeny)
        {
            mutationOrder = GrantMutationOrder.RemoveSaveAdd;
        }
        else
        {
            var previousRights = priorEntry.SavedRights ?? SavedRightsState.DefaultForMode(priorEntry.IsDeny);
            var nextRights = newEntry.SavedRights ?? SavedRightsState.DefaultForMode(newEntry.IsDeny);
            bool hasAdditions =
                (nextRights.Execute && !previousRights.Execute) ||
                (nextRights.Write && !previousRights.Write) ||
                (nextRights.Read && !previousRights.Read) ||
                (nextRights.Special && !previousRights.Special) ||
                (nextRights.Own && !previousRights.Own);
            bool hasRemovals =
                (previousRights.Execute && !nextRights.Execute) ||
                (previousRights.Write && !nextRights.Write) ||
                (previousRights.Read && !nextRights.Read) ||
                (previousRights.Special && !nextRights.Special) ||
                (previousRights.Own && !nextRights.Own);
            mutationOrder = hasAdditions && hasRemovals
                ? GrantMutationOrder.RemoveSaveAdd
                : hasRemovals
                    ? GrantMutationOrder.ApplyThenSave
                    : GrantMutationOrder.SaveThenApply;
        }

        string? ownerSid = ResolveOwnerSid(accountSid, newEntry.IsDeny, newEntry.SavedRights);
        bool resetOwnerOnRemoval = mutationOrder == GrantMutationOrder.RemoveSaveAdd &&
            priorEntry != null &&
            !priorEntry.IsDeny &&
            priorEntry.SavedRights?.Own == true &&
            (newEntry.IsDeny || newEntry.SavedRights?.Own != true);
        bool resetOwnerAfterApply = ShouldResetOwner(priorEntry, newEntry) && !resetOwnerOnRemoval;
        var ownerRollbackState = CaptureOwnerRollbackState(
            normalizedPath,
            ownerSid,
            resetOwnerOnRemoval || resetOwnerAfterApply);
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
                        TryRestoreGrantSnapshots(accountSid, normalizedPath, snapshots, ex);
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
                    snapshots);
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
                    snapshots);

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
                        TryRollbackGrantAcl(
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
                        TryRollbackGrantAcl(
                            accountSid,
                            normalizedPath,
                            priorEntry,
                            newEntry,
                            ownerRollbackState,
                            primaryConfigPath,
                            ex,
                            removeNewEntryBeforeRestore: false);
                        TryRestoreGrantSnapshots(accountSid, normalizedPath, snapshots, ex);
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
        OwnerRollbackState ownerRollbackState,
        string? primaryConfigPath,
        bool storeModified,
        IReadOnlyList<GrantIntentSnapshot> snapshots,
        bool applyFromRemovalState = false)
    {
        try
        {
            ApplyRuntimeGrantChange(
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
            TryRollbackGrantAcl(
                accountSid,
                normalizedPath,
                priorEntry,
                newEntry,
                ownerRollbackState,
                primaryConfigPath,
                operationException,
                removeNewEntryBeforeRestore: true);
            if (storeModified)
                TryRestoreGrantSnapshots(accountSid, normalizedPath, snapshots, operationException);
            throw operationException;
        }
    }

    private List<GrantIntentLocation> GetGrantLocationsForPath(string sid, string normalizedPath)
        => GrantIntentRepository.FindEntriesForSid(sid)
            .Where(location =>
                !location.Entry.IsTraverseOnly &&
                string.Equals(location.Entry.Path, normalizedPath, StringComparison.OrdinalIgnoreCase))
            .ToList();

    private List<GrantIntentLocation> GetAllGrantLocations(string sid)
        => GrantIntentRepository.FindEntriesForSid(sid)
            .Where(location => !location.Entry.IsTraverseOnly)
            .ToList();

    private static IEnumerable<(string Path, bool IsDeny, GrantedPathEntry Entry, IReadOnlyList<GrantIntentLocation> Locations)>
        GroupGrantLocationsByPath(IEnumerable<GrantIntentLocation> locations)
        => locations
            .GroupBy(location => (location.Entry.Path, location.Entry.IsDeny))
            .Select(group => (group.Key.Path, group.Key.IsDeny, group.First().Entry, (IReadOnlyList<GrantIntentLocation>)group.ToList()));

    private static IEnumerable<(string Path, IReadOnlyList<GrantIntentLocation> Locations)>
        GroupTraverseLocationsByPath(IEnumerable<GrantIntentLocation> locations)
        => locations
            .GroupBy(location => location.Entry.Path, StringComparer.OrdinalIgnoreCase)
            .Select(group => (group.Key, (IReadOnlyList<GrantIntentLocation>)group.ToList()));

    private List<IGrantIntentStore> ResolveFinalStores(
        IGrantIntentStore? selectedStore,
        IReadOnlyList<GrantIntentLocation> sameModeLocations,
        IReadOnlyList<GrantIntentLocation> oppositeModeLocations,
        bool allowOppositeModeSwitch)
    {
        if (selectedStore != null)
            return [selectedStore];

        var existingLocations = allowOppositeModeSwitch && oppositeModeLocations.Count > 0
            ? oppositeModeLocations
            : sameModeLocations;
        if (existingLocations.Count > 0)
            return existingLocations.Select(location => location.Store).Distinct().ToList();

        return [MainGrantIntentStore];
    }

    private bool WouldMutateGrantStores(
        GrantedPathEntry newEntry,
        IReadOnlyList<GrantIntentLocation> allLocations,
        IReadOnlyList<IGrantIntentStore> finalStores)
    {
        var finalStoreSet = finalStores.ToHashSet();

        foreach (var location in allLocations)
        {
            if (!finalStoreSet.Contains(location.Store) ||
                location.Entry.IsDeny != newEntry.IsDeny)
            {
                return true;
            }
        }

        foreach (var targetStore in finalStores)
        {
            var currentEntry = allLocations.FirstOrDefault(location =>
                ReferenceEquals(location.Store, targetStore) &&
                location.Entry.IsDeny == newEntry.IsDeny)?.Entry;
            if (currentEntry == null)
            {
                return true;
            }

            if (!traverseGrantStateService.EntriesEquivalent(currentEntry, newEntry))
                return true;
        }

        return false;
    }

    private void MutateGrantStores(
        string sid,
        string normalizedPath,
        GrantedPathEntry newEntry,
        IReadOnlyList<GrantIntentLocation> allLocations,
        IReadOnlyList<IGrantIntentStore> finalStores)
    {
        var finalStoreSet = finalStores.ToHashSet();

        foreach (var location in allLocations)
        {
            if (!finalStoreSet.Contains(location.Store) ||
                location.Entry.IsDeny != newEntry.IsDeny)
            {
                location.Store.RemoveEntry(sid, location.Entry);
            }
        }

        foreach (var targetStore in finalStores)
        {
            var currentEntry = allLocations.FirstOrDefault(location =>
                ReferenceEquals(location.Store, targetStore) &&
                location.Entry.IsDeny == newEntry.IsDeny)?.Entry;
            if (currentEntry == null)
            {
                targetStore.AddEntry(sid, newEntry);
                continue;
            }

            if (!traverseGrantStateService.EntriesEquivalent(currentEntry, newEntry))
                targetStore.ReplaceEntry(sid, currentEntry, newEntry);
        }
    }

    private bool RestoreGrantStoresToExactLocations(
        string sid,
        IReadOnlyList<GrantIntentLocation> currentLocations,
        IReadOnlyList<GrantIntentRestoreLocation> desiredLocations,
        bool mutate)
    {
        var desiredByConfigPath = desiredLocations
            .GroupBy(location => NormalizeConfigPath(location.ConfigPath), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        bool modified = false;

        foreach (var location in currentLocations)
        {
            var configPath = NormalizeConfigPath(location.Store.ConfigPath);
            if (!desiredByConfigPath.ContainsKey(configPath))
            {
                modified = true;
                if (mutate)
                    location.Store.RemoveEntry(sid, location.Entry);
            }
        }

        foreach (var desired in desiredByConfigPath.Values)
        {
            var targetStore = GrantIntentStoreProvider.ResolveStore(desired.ConfigPath);
            var currentEntry = currentLocations.FirstOrDefault(location =>
                string.Equals(NormalizeConfigPath(location.Store.ConfigPath), NormalizeConfigPath(desired.ConfigPath), StringComparison.OrdinalIgnoreCase))?.Entry;
            if (currentEntry == null)
            {
                modified = true;
                if (mutate)
                    targetStore.AddEntry(sid, desired.Entry);
                continue;
            }

            if (!traverseGrantStateService.EntriesEquivalent(currentEntry, desired.Entry))
            {
                modified = true;
                if (mutate)
                    targetStore.ReplaceEntry(sid, currentEntry, desired.Entry);
            }
        }

        return modified;
    }

    private static string NormalizeConfigPath(string? configPath)
        => configPath == null ? string.Empty : Path.GetFullPath(configPath);

    private IReadOnlyList<GrantIntentSnapshot> CaptureGrantSnapshots(
        string sid,
        string normalizedPath,
        IEnumerable<IGrantIntentStore> stores)
        => stores
            .Distinct()
            .Select(store => new GrantIntentSnapshot(
                store,
                store.GetEntries(sid)
                    .Where(entry =>
                        !entry.IsTraverseOnly &&
                        string.Equals(entry.Path, normalizedPath, StringComparison.OrdinalIgnoreCase))
                    .Select(entry => entry.Clone())
                    .ToList()))
            .ToList();

    private void RestoreGrantSnapshots(
        string sid,
        string normalizedPath,
        IReadOnlyList<GrantIntentSnapshot> snapshots)
    {
        foreach (var snapshot in snapshots)
        {
            var currentEntries = snapshot.Store.GetEntries(sid)
                .Where(entry =>
                    !entry.IsTraverseOnly &&
                    string.Equals(entry.Path, normalizedPath, StringComparison.OrdinalIgnoreCase))
                .ToList();
            foreach (var entry in currentEntries)
                snapshot.Store.RemoveEntry(sid, entry);

            foreach (var entry in snapshot.Entries)
                snapshot.Store.AddEntry(sid, entry);
        }
    }

    private void TryRestoreGrantSnapshots(
        string sid,
        string normalizedPath,
        IReadOnlyList<GrantIntentSnapshot> snapshots,
        GrantOperationException operationException)
    {
        try
        {
            RestoreGrantSnapshots(sid, normalizedPath, snapshots);
            grantIntentStoreSaveService.Save(
                snapshots.Select(snapshot => snapshot.Store),
                GrantApplyFailureStep.RevertIntentSave,
                normalizedPath);
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
                grantIntentStoreSaveService.GetPrimaryConfigPath(snapshots.Select(snapshot => snapshot.Store)),
                ex);
        }
    }

    private void TryRestoreTraverseSnapshots(
        string ownerSid,
        string normalizedPath,
        IReadOnlyList<TraverseGrantStateService.StoreSnapshot> snapshots,
        GrantOperationException operationException)
    {
        try
        {
            traverseGrantStateService.RestoreStoreSnapshots(ownerSid, normalizedPath, snapshots);
            grantIntentStoreSaveService.Save(
                snapshots.Select(snapshot => snapshot.Store),
                GrantApplyFailureStep.RevertIntentSave,
                normalizedPath);
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
                grantIntentStoreSaveService.GetPrimaryConfigPath(snapshots.Select(snapshot => snapshot.Store)),
                ex);
        }
    }

    private static void RemoveGrantEntries(string sid, string normalizedPath, IEnumerable<GrantIntentLocation> locations)
    {
        foreach (var location in locations)
        {
            if (!string.Equals(location.Entry.Path, normalizedPath, StringComparison.OrdinalIgnoreCase))
                continue;

            location.Store.RemoveEntry(sid, location.Entry);
        }
    }

    private void RemoveTraverseEntriesFromStores(
        string sid,
        string normalizedPath,
        IEnumerable<GrantIntentLocation> locations)
    {
        foreach (var location in locations)
        {
            if (!string.Equals(location.Entry.Path, normalizedPath, StringComparison.OrdinalIgnoreCase))
                continue;

            traverseIntentStoreCoordinator.RemoveTraverseEntryFromStore(sid, location.Store, location.Entry);
        }
    }

    private void TryRestoreTrackedRuntimeTraverseEntry(
        string sid,
        string normalizedPath,
        GrantedPathEntry? snapshot,
        GrantOperationException operationException)
    {
        try
        {
            dbAccessor.Write(db =>
            {
                var entries = GetRuntimeTraverseStore(db, sid, createIfMissing: snapshot != null);
                var currentEntry = FindRuntimeTraverseEntry(
                    db,
                    sid,
                    normalizedPath,
                    includeManualSharedEntries: true);
                if (currentEntry != null)
                    entries.Remove(currentEntry);

                if (snapshot != null)
                    entries.Add(snapshot.Clone());

                if (!AclHelper.IsSpecificContainerSid(sid))
                    db.RemoveAccountIfEmpty(sid);
            });
        }
        catch (Exception ex)
        {
            operationException.AppendCleanupFailure(
                GrantApplyFailureStep.RevertIntentSave,
                normalizedPath,
                null,
                ex);
        }
    }

    private void TryRollbackTraverseAcl(
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

    private static string GetRepresentativePath(
        IReadOnlyList<GrantIntentLocation> grantLocations,
        IReadOnlyList<GrantIntentLocation> traverseLocations)
        => grantLocations.Select(location => location.Entry.Path)
            .Concat(traverseLocations.Select(location => location.Entry.Path))
            .FirstOrDefault() ?? string.Empty;

    private List<GrantedPathEntry> GetRuntimeTraverseStore(
        AppDatabase database,
        string sid,
        bool createIfMissing)
        => createIfMissing
            ? traverseIntentStoreCoordinator.GetOrCreateTraverseStore(database, sid)
            : traverseIntentStoreCoordinator.GetTraverseStoreOrEmpty(database, sid);

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

    private void ApplyRuntimeGrantChange(
        string accountSid,
        GrantedPathEntry? priorEntry,
        GrantedPathEntry newEntry,
        string? ownerSid,
        bool shouldResetOwner,
        bool preferExistingAddSemantics)
    {
        if (priorEntry == null)
        {
            fileSystemOperations.AddGrant(
                accountSid,
                newEntry.Path,
                newEntry.IsDeny,
                newEntry.SavedRights,
                ownerSid,
                desiredPreviousSaclLabel: newEntry.PreviousSaclLabel);
        }
        else if (priorEntry.IsDeny == newEntry.IsDeny)
        {
            if (preferExistingAddSemantics && traverseGrantStateService.EntriesEquivalent(priorEntry, newEntry))
            {
                fileSystemOperations.AddGrant(
                    accountSid,
                    newEntry.Path,
                    newEntry.IsDeny,
                    newEntry.SavedRights,
                    ownerSid,
                    desiredPreviousSaclLabel: newEntry.PreviousSaclLabel);
            }
            else if (HasRuntimeGrantEntry(accountSid, newEntry.Path, newEntry.IsDeny))
            {
                fileSystemOperations.UpdateGrant(
                    accountSid,
                    newEntry.Path,
                    newEntry.IsDeny,
                    newEntry.SavedRights!,
                    ownerSid,
                    previousSavedRights: priorEntry.SavedRights,
                    previousSaclLabel: priorEntry.PreviousSaclLabel,
                    desiredPreviousSaclLabel: newEntry.PreviousSaclLabel);
            }
            else
            {
                fileSystemOperations.AddGrant(
                    accountSid,
                    newEntry.Path,
                    newEntry.IsDeny,
                    newEntry.SavedRights,
                    ownerSid,
                    desiredPreviousSaclLabel: newEntry.PreviousSaclLabel);
            }
        }
        else
        {
            RemoveRuntimeGrant(accountSid, newEntry.Path, priorEntry, updateFileSystem: true);
            fileSystemOperations.AddGrant(
                accountSid,
                newEntry.Path,
                newEntry.IsDeny,
                newEntry.SavedRights,
                ownerSid,
                desiredPreviousSaclLabel: newEntry.PreviousSaclLabel);
        }

        if (shouldResetOwner)
            fileSystemOperations.ResetOwner(newEntry.Path, recursive: false);

        if (!newEntry.IsDeny)
            ApplyAllowGrantSideEffects(accountSid, newEntry.Path, newEntry.SavedRights!);
    }

    private void TryRollbackGrantAcl(
        string accountSid,
        string normalizedPath,
        GrantedPathEntry? priorEntry,
        GrantedPathEntry newEntry,
        OwnerRollbackState ownerRollbackState,
        string? primaryConfigPath,
        GrantOperationException operationException,
        bool removeNewEntryBeforeRestore)
    {
        try
        {
            if (priorEntry == null)
            {
                RemoveRuntimeGrant(accountSid, normalizedPath, newEntry, updateFileSystem: true);
                return;
            }

            if (priorEntry.IsDeny == newEntry.IsDeny)
            {
                if (HasRuntimeGrantEntry(accountSid, normalizedPath, priorEntry.IsDeny))
                {
                    fileSystemOperations.UpdateGrant(
                        accountSid,
                        normalizedPath,
                        priorEntry.IsDeny,
                        priorEntry.SavedRights ?? SavedRightsState.DefaultForMode(priorEntry.IsDeny),
                        ResolveOwnerSid(accountSid, priorEntry.IsDeny, priorEntry.SavedRights),
                        desiredPreviousSaclLabel: priorEntry.PreviousSaclLabel);
                }
                else
                {
                    fileSystemOperations.AddGrant(
                        accountSid,
                        normalizedPath,
                        priorEntry.IsDeny,
                        priorEntry.SavedRights,
                        ResolveOwnerSid(accountSid, priorEntry.IsDeny, priorEntry.SavedRights),
                        desiredPreviousSaclLabel: priorEntry.PreviousSaclLabel);
                    if (!priorEntry.IsDeny)
                        ApplyAllowGrantSideEffects(
                            accountSid,
                            normalizedPath,
                            priorEntry.SavedRights ?? SavedRightsState.DefaultForMode(isDeny: false));
                }

                RestoreOwnerAfterRollback(normalizedPath, priorEntry, ownerRollbackState);
                return;
            }

            if (removeNewEntryBeforeRestore)
                RemoveRuntimeGrant(accountSid, normalizedPath, newEntry, updateFileSystem: true);
            fileSystemOperations.AddGrant(
                accountSid,
                normalizedPath,
                priorEntry.IsDeny,
                priorEntry.SavedRights,
                ResolveOwnerSid(accountSid, priorEntry.IsDeny, priorEntry.SavedRights),
                desiredPreviousSaclLabel: priorEntry.PreviousSaclLabel);
            if (!priorEntry.IsDeny)
                ApplyAllowGrantSideEffects(
                    accountSid,
                    normalizedPath,
                    priorEntry.SavedRights ?? SavedRightsState.DefaultForMode(isDeny: false));
            RestoreOwnerAfterRollback(normalizedPath, priorEntry, ownerRollbackState);
        }
        catch (Exception ex)
        {
            operationException.AppendCleanupFailure(
                GrantApplyFailureStep.GrantAclRollback,
                normalizedPath,
                primaryConfigPath,
                ex);
        }
    }

    private bool HasRuntimeGrantEntry(string sid, string path, bool isDeny)
    {
        var normalizedPath = Path.GetFullPath(path);
        return dbAccessor.Read(db =>
            GrantCoreOperations.FindGrantEntryInDb(db, sid, normalizedPath, isDeny) != null);
    }

    private OwnerRollbackState CaptureOwnerRollbackState(
        string path,
        string? ownerSid,
        bool shouldResetOwner)
    {
        var ownerMayHaveChanged = ownerSid != null || shouldResetOwner;
        if (!ownerMayHaveChanged)
            return new OwnerRollbackState(false, null);

        var security = aclAccessor.GetSecurity(path);
        var ownerIdentity = security.GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
        return new OwnerRollbackState(true, ownerIdentity?.Value);
    }

    private void RestoreOwnerAfterRollback(
        string path,
        GrantedPathEntry priorEntry,
        OwnerRollbackState ownerRollbackState)
    {
        if (!string.IsNullOrEmpty(ownerRollbackState.OriginalOwnerSid))
        {
            fileSystemOperations.ChangeOwner(path, ownerRollbackState.OriginalOwnerSid, recursive: false);
            return;
        }

        var rights = priorEntry.SavedRights ?? SavedRightsState.DefaultForMode(priorEntry.IsDeny);
        if (!ownerRollbackState.OwnerMayHaveChanged && !(priorEntry.IsDeny && rights.Own))
            return;

        if (priorEntry.IsDeny && rights.Own)
            fileSystemOperations.ResetOwner(path, recursive: false);
    }

    private void RemoveRuntimeGrant(
        string sid,
        string path,
        GrantedPathEntry entry,
        bool updateFileSystem)
    {
        bool removed = fileSystemOperations.RemoveGrant(sid, path, entry.IsDeny, updateFileSystem);

        if (!removed && updateFileSystem)
        {
            var normalizedPath = Path.GetFullPath(path);
            grantAceService.RevertAce(normalizedPath, sid, entry.IsDeny);

            if (!entry.IsDeny && AclHelper.IsLowIntegritySid(sid) && entry.SavedRights?.Write == true)
                mandatoryLabelService.RestoreMandatoryLabel(normalizedPath, entry.PreviousSaclLabel);
        }

        if (!entry.IsDeny)
            CleanupDerivedGrantSources(sid, Path.GetFullPath(path), updateFileSystem);
    }

    private void RemoveTrackedGrantWithoutFilesystem(
        string sid,
        string normalizedPath,
        GrantedPathEntry entry,
        bool cleanupDerivedSources)
    {
        RemoveRuntimeGrant(sid, normalizedPath, entry, updateFileSystem: false);
        if (!cleanupDerivedSources || entry.IsDeny)
            return;
    }

    private void CleanupDerivedGrantSources(string sid, string normalizedPath, bool updateFileSystem)
    {
        if (AclHelper.IsContainerSid(sid))
        {
            containerIuSync.RevertInteractiveUserGrant(sid, normalizedPath);
            return;
        }

        if (!AclHelper.IsLowIntegritySid(sid))
            lowIntegrityGrantSync.RevertSource(sid, normalizedPath, updateFileSystem);
    }

    private void ApplyAllowGrantSideEffects(string sid, string normalizedPath, SavedRightsState rights)
    {
        var traverseDir = pathInfo.DirectoryExists(normalizedPath)
            ? normalizedPath
            : Path.GetDirectoryName(normalizedPath);
        if (!string.IsNullOrEmpty(traverseDir))
            traverseCore.AddTraverse(sid, traverseDir);

        if (AclHelper.IsContainerSid(sid))
        {
            containerIuSync.SyncAllowGrantToInteractiveUser(sid, normalizedPath, rights);
            return;
        }

        if (AclHelper.IsLowIntegritySid(sid))
        {
            var sources = dbAccessor.Read(db =>
                db.Accounts
                    .Where(a => !AclHelper.IsLowIntegritySid(a.Sid) && !AclHelper.IsContainerSid(a.Sid))
                    .Where(a => a.Grants.Any(g => !g.IsTraverseOnly && !g.IsDeny &&
                        string.Equals(g.Path, normalizedPath, StringComparison.OrdinalIgnoreCase)))
                    .Select(a => a.Sid)
                    .ToList());
            if (sources.Count > 0)
            {
                dbAccessor.Write(db =>
                {
                    var entry = GrantCoreOperations.FindGrantEntryInDb(db, sid, normalizedPath, isDeny: false);
                    if (entry != null)
                        entry.SourceSids = sources;
                });
            }

            return;
        }

        dbAccessor.Write(db =>
        {
            var lowIlEntry = GrantCoreOperations.FindGrantEntryInDb(
                db, AclHelper.LowIntegritySid, normalizedPath, isDeny: false);
            if (lowIlEntry?.SourceSids != null &&
                !lowIlEntry.SourceSids.Contains(sid, StringComparer.OrdinalIgnoreCase))
                lowIlEntry.SourceSids.Add(sid);
        });
    }

    private void RemoveTrackedTraverseWithoutFilesystem(string sid, string normalizedPath)
    {
        bool removed = RemoveTraverseWithoutPersisting(sid, normalizedPath, updateFileSystem: false);

        if (!removed && AclHelper.IsContainerSid(sid))
            containerIuSync.RevertInteractiveUserTraverse(sid, normalizedPath);
    }

    private RuntimeEntrySnapshot CaptureRuntimeGrantSnapshot(string sid, string path, bool isDeny)
    {
        var normalizedPath = Path.GetFullPath(path);
        var entry = dbAccessor.Read(db =>
            GrantCoreOperations.FindGrantEntryInDb(db, sid, normalizedPath, isDeny)?.Clone());
        return new RuntimeEntrySnapshot(sid, normalizedPath, IsTraverseOnly: false, isDeny, entry);
    }

    private RuntimeEntrySnapshot CaptureRuntimeTraverseSnapshot(string sid, string path)
    {
        var normalizedPath = Path.GetFullPath(path);
        var entry = dbAccessor.Read(db =>
            FindRuntimeTraverseEntry(db, sid, normalizedPath, includeManualSharedEntries: true)?.Clone());
        return new RuntimeEntrySnapshot(sid, normalizedPath, IsTraverseOnly: true, IsDeny: false, entry);
    }

    private IReadOnlyList<RuntimeEntrySnapshot> CaptureLinkedRuntimeEntrySnapshots(
        string path,
        bool isTraverseOnly,
        string sourceSid)
    {
        var normalizedPath = Path.GetFullPath(path);
        return dbAccessor.Read(db =>
            db.Accounts
                .Where(account => !AclHelper.IsContainerSid(account.Sid) && !AclHelper.IsLowIntegritySid(account.Sid))
                .Select(account => new
                {
                    account.Sid,
                    Entry = account.Grants.FirstOrDefault(entry =>
                        entry.IsTraverseOnly == isTraverseOnly &&
                        !entry.IsDeny &&
                        string.Equals(entry.Path, normalizedPath, StringComparison.OrdinalIgnoreCase) &&
                        IsRuntimeEntryLinkedToSource(entry, sourceSid))
                })
                .Where(match => match.Entry != null)
                .Select(match => new RuntimeEntrySnapshot(
                    match.Sid,
                    normalizedPath,
                    isTraverseOnly,
                    IsDeny: false,
                    match.Entry!.Clone()))
                .ToList());
    }

    private void RestoreRuntimeGrantSnapshot(RuntimeEntrySnapshot snapshot)
    {
        dbAccessor.Write(db =>
        {
            var grants = snapshot.Entry != null
                ? db.GetOrCreateAccount(snapshot.Sid).Grants
                : db.GetAccount(snapshot.Sid)?.Grants;
            if (grants == null)
                return;

            var current = GrantCoreOperations.FindGrantEntryInList(grants, snapshot.Path, snapshot.IsDeny);
            if (current != null)
                grants.Remove(current);

            if (snapshot.Entry != null)
                grants.Add(snapshot.Entry.Clone());

            db.RemoveAccountIfEmpty(snapshot.Sid);
        });
    }

    private void RestoreRuntimeTraverseSnapshot(RuntimeEntrySnapshot snapshot)
    {
        dbAccessor.Write(db =>
        {
            var entries = GetRuntimeTraverseStore(db, snapshot.Sid, createIfMissing: snapshot.Entry != null);
            var current = FindRuntimeTraverseEntry(
                db,
                snapshot.Sid,
                snapshot.Path,
                includeManualSharedEntries: true);
            if (current != null)
                entries.Remove(current);

            if (snapshot.Entry != null)
                entries.Add(snapshot.Entry.Clone());

            if (!AclHelper.IsSpecificContainerSid(snapshot.Sid))
                db.RemoveAccountIfEmpty(snapshot.Sid);
        });
    }

    private GrantedPathEntry? FindRuntimeTraverseEntry(
        AppDatabase database,
        string sid,
        string normalizedPath,
        bool includeManualSharedEntries)
        => traverseGrantOwnerResolver.FindTraverseEntry(
            database,
            sid,
            normalizedPath,
            includeManualSharedEntries);

    private void RestoreLinkedRuntimeEntrySnapshots(
        string path,
        bool isTraverseOnly,
        string sourceSid,
        IReadOnlyList<RuntimeEntrySnapshot> snapshots)
    {
        var normalizedPath = Path.GetFullPath(path);
        var snapshotBySid = snapshots.ToDictionary(snapshot => snapshot.Sid, StringComparer.OrdinalIgnoreCase);

        dbAccessor.Write(db =>
        {
            var candidateSids = db.Accounts
                .Where(account => !AclHelper.IsContainerSid(account.Sid) && !AclHelper.IsLowIntegritySid(account.Sid))
                .Select(account => account.Sid)
                .Concat(snapshotBySid.Keys)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var candidateSid in candidateSids)
            {
                var account = db.GetAccount(candidateSid);
                var grants = account?.Grants;
                var current = grants?.FirstOrDefault(entry =>
                    entry.IsTraverseOnly == isTraverseOnly &&
                    !entry.IsDeny &&
                    string.Equals(entry.Path, normalizedPath, StringComparison.OrdinalIgnoreCase) &&
                    IsRuntimeEntryLinkedToSource(entry, sourceSid));
                if (current != null)
                    grants!.Remove(current);

                if (!snapshotBySid.TryGetValue(candidateSid, out var snapshot) || snapshot.Entry == null)
                {
                    if (account != null)
                        db.RemoveAccountIfEmpty(candidateSid);
                    continue;
                }

                db.GetOrCreateAccount(candidateSid).Grants.Add(snapshot.Entry.Clone());
            }
        });
    }

    private static bool IsRuntimeEntryLinkedToSource(GrantedPathEntry entry, string sourceSid)
        => entry.SourceSids?.Contains(sourceSid, StringComparer.OrdinalIgnoreCase) == true ||
           string.Equals(entry.OwnerContainerSid, sourceSid, StringComparison.OrdinalIgnoreCase);
}
