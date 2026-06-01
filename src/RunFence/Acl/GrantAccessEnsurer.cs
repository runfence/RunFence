using System.Security.AccessControl;
using RunFence.Acl.Permissions;
using RunFence.Acl.Traverse;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Persistence;
namespace RunFence.Acl;

/// <summary>
/// Confirm-callback and conflict-resolution workflows for ensuring effective access.
/// </summary>
public class GrantAccessEnsurer(
    IAclPermissionService aclPermission,
    UiThreadDatabaseAccessor dbAccessor,
    IPathSecurityDescriptorAccessor aclAccessor,
    IFileSystemPathInfo pathInfo,
    ITraverseCoreOperations traverseCore,
    GrantFileSystemOperations fileSystemOperations,
    IInteractiveUserResolver interactiveUserResolver,
    ITraverseGrantOwnerResolver traverseGrantOwnerResolver,
    Func<IGrantIntentRepository> grantIntentRepository,
    Func<IGrantIntentStore> mainGrantIntentStore,
    IGrantIntentStoreSaveService grantIntentStoreSaveService,
    GrantIntentMutationStateRestorer grantIntentMutationStateRestorer,
    GrantRuntimeSnapshotService grantRuntimeSnapshotService)
{
    private readonly record struct EnsureAccessPlan(
        string Normalized,
        bool IsFolder,
        bool PathExists,
        string? RequestedContainerSid,
        IReadOnlyList<EnsureIdentityPlan> Identities);

    private readonly record struct EnsureIdentityPlan(
        string Sid,
        SavedRightsState RequestedRights,
        SavedRightsState TargetRights,
        FileSystemRights TargetFsRights,
        bool TargetRightsExpansionRequiresPersistence,
        bool EffectiveTargetAccessSufficient,
        bool TargetGrantRequired,
        bool TargetGrantDbRequired,
        bool TargetAceRequired,
        bool EffectiveTraverseAccessSufficient,
        bool TraverseDbRequired,
        bool TraverseAceRequired,
        string? TraverseDir,
        IReadOnlyList<string> TraverseCoveragePaths,
        IReadOnlyList<string> TraversePathsToApply,
        DenyConflictPlan? DenyConflict);

    private readonly record struct DenyConflictPlan(
        string Sid,
        string Path,
        SavedRightsState OriginalRights,
        SavedRightsState UpdatedRights,
        bool RemoveEntry,
        IReadOnlyList<GrantIntentLocation> Locations);

    private readonly record struct StoreSelection(
        string OwnerSid,
        IReadOnlyList<IGrantIntentStore> Stores);

    private readonly record struct PersistedIdentityContext(
        EnsureIdentityPlan Plan,
        StoreSelection TargetSelection,
        StoreSelection? TraverseSelection,
        GrantRuntimeEntrySnapshot GrantSnapshot,
        GrantRuntimeEntrySnapshot? TraverseSnapshot,
        IReadOnlyList<GrantIntentStoreSnapshot> StoreSnapshots);

    private readonly record struct DenyConflictContext(
        DenyConflictPlan Plan,
        GrantRuntimeEntrySnapshot RuntimeSnapshot,
        IReadOnlyList<GrantIntentStoreSnapshot> StoreSnapshots);

    private IGrantIntentRepository GrantIntentRepository => grantIntentRepository();

    private IGrantIntentStore MainGrantIntentStore => mainGrantIntentStore();

    public GrantApplyResult EnsureAccess(string sid, string path, SavedRightsState savedRights,
        Func<string, string, bool>? confirm = null, bool unelevated = false)
        => EnsureAccessCore(
            sid,
            path,
            savedRights,
            confirm,
            unelevated,
            true);

    public GrantApplyResult EnsureTemporaryAccess(string sid, string path, SavedRightsState savedRights,
        Func<string, string, bool>? confirm = null, bool unelevated = false)
        => EnsureAccessCore(
            sid,
            path,
            savedRights,
            confirm,
            unelevated,
            false);

    public GrantApplyResult EnsureTemporaryAccess(string sid, string path, FileSystemRights rights,
        Func<string, string, bool>? confirm = null, bool unelevated = false)
    {
        var normalized = Path.GetFullPath(path);
        var (isFolder, _) = ResolvePathState(normalized);
        var savedRights = GrantRightsMapper.FromRights(rights, isFolder, isDeny: false);
        return EnsureTemporaryAccess(sid, normalized, savedRights, confirm, unelevated);
    }

    public GrantApplyResult EnsureAccess(string sid, string path, FileSystemRights rights,
        Func<string, string, bool>? confirm = null, bool unelevated = false)
    {
        var normalized = Path.GetFullPath(path);
        var (isFolder, _) = ResolvePathState(normalized);
        var savedRights = GrantRightsMapper.FromRights(rights, isFolder, isDeny: false);
        return EnsureAccess(sid, normalized, savedRights, confirm, unelevated);
    }

    private GrantApplyResult EnsureAccessCore(
        string sid,
        string path,
        SavedRightsState savedRights,
        Func<string, string, bool>? confirm,
        bool unelevated,
        bool persistTargetGrantIntent)
    {
        var plan = BuildPlan(sid, path, savedRights, unelevated, persistTargetGrantIntent);
        ConfirmAccessPlan(plan, confirm);
        if (!persistTargetGrantIntent &&
            plan.Identities.Any(identity =>
                !identity.EffectiveTargetAccessSufficient &&
                identity.TargetRightsExpansionRequiresPersistence))
        {
            throw new InvalidOperationException(
                $"Temporary access cannot widen the tracked durable grant on '{plan.Normalized}'. Use EnsureAccess to persist the new rights.");
        }

        var denyConflictContexts = ApplyPersistedDenyConflicts(plan);
        return ExecutePersistedPlan(plan, denyConflictContexts, unelevated, persistTargetGrantIntent);
    }

    private EnsureAccessPlan BuildPlan(
        string requestedSid,
        string path,
        SavedRightsState requestedRights,
        bool unelevated,
        bool persistTargetGrantIntent)
    {
        var normalized = Path.GetFullPath(path);
        var (isFolder, pathExists) = ResolvePathState(normalized);
        var identities = new List<EnsureIdentityPlan>();

        foreach (var identitySid in ResolveIdentitySids(requestedSid))
        {
            var requiredRights = GrantRightsMapper.MapAllowRights(requestedRights, isFolder);
            if (pathExists &&
                string.Equals(identitySid, requestedSid, StringComparison.OrdinalIgnoreCase) &&
                AclHelper.IsSpecificContainerSid(identitySid) &&
                !aclPermission.NeedsPermissionGrant(
                    normalized,
                    TraverseEntryLookup.ResolveAclSid(identitySid),
                    requiredRights,
                    unelevated))
            {
                continue;
            }

            identities.Add(BuildIdentityPlan(
                identitySid,
                normalized,
                requestedRights,
                isFolder,
                pathExists,
                requiredRights,
                unelevated,
                persistTargetGrantIntent));
        }

        return new EnsureAccessPlan(
            Normalized: normalized,
            IsFolder: isFolder,
            PathExists: pathExists,
            RequestedContainerSid: AclHelper.IsSpecificContainerSid(requestedSid) ? requestedSid : null,
            Identities: identities);
    }

    private EnsureIdentityPlan BuildIdentityPlan(
        string identitySid,
        string normalized,
        SavedRightsState requestedRights,
        bool isFolder,
        bool pathExists,
        FileSystemRights requiredRights,
        bool unelevated,
        bool persistTargetGrantIntent)
    {
        var dbState = dbAccessor.Read(db =>
        {
            var existing = GrantEntryLookup.FindGrantEntryInDb(db, identitySid, normalized, isDeny: false);
            var existingRights = existing?.SavedRights ?? SavedRightsState.DefaultForMode(isDeny: false);
            var expandedTargetRights = existing == null
                ? requestedRights
                : new SavedRightsState(
                    Execute: existing.SavedRights?.Execute == true || requestedRights.Execute,
                    Write: existing.SavedRights?.Write == true || requestedRights.Write,
                    Read: true,
                    Special: existing.SavedRights?.Special == true || requestedRights.Special,
                    Own: existing.SavedRights?.Own == true || requestedRights.Own);
            var targetRights = existing == null
                ? requestedRights
                : !persistTargetGrantIntent
                    ? existingRights
                    : expandedTargetRights;
            var targetFsRights = GrantRightsMapper.MapAllowRights(targetRights, isFolder);
            var traverseDir = isFolder ? normalized : Path.GetDirectoryName(normalized);
            var hasTraverseEntry = !string.IsNullOrEmpty(traverseDir) &&
                                   traverseGrantOwnerResolver.FindTraverseEntry(db, identitySid, traverseDir) != null;
            var denyEntry = GrantEntryLookup.FindGrantEntryInDb(db, identitySid, normalized, isDeny: true);

            return new
            {
                ExistingTarget = existing,
                ExpandedTargetRights = expandedTargetRights,
                TargetRights = targetRights,
                TargetFsRights = targetFsRights,
                HasTraverseEntry = hasTraverseEntry,
                TraverseDir = traverseDir,
                DenyEntry = denyEntry
            };
        });
        var existingTargetRights = dbState.ExistingTarget?.SavedRights ?? SavedRightsState.DefaultForMode(isDeny: false);
        var targetRightsExpansionRequiresPersistence = dbState.ExistingTarget != null &&
                                                      ((requestedRights.Execute && !existingTargetRights.Execute) ||
                                                       (requestedRights.Write && !existingTargetRights.Write) ||
                                                       (requestedRights.Special && !existingTargetRights.Special) ||
                                                       (requestedRights.Own && !existingTargetRights.Own));
        var effectiveTargetAccessSufficient = !pathExists ||
            !aclPermission.NeedsPermissionGrant(normalized, identitySid, requiredRights, unelevated);

        var groupSids = aclPermission.ResolveAccountGroupSids(identitySid);
        var trackedGrantNeedsFix = false;

        if (dbState.ExistingTarget != null && pathExists)
        {
            var state = fileSystemOperations.ReadGrantState(normalized, identitySid, groupSids);
            if (state.DirectAllowAceCount == 0)
            {
                trackedGrantNeedsFix = true;
            }
            else
            {
                var diskRights = GrantRightsMapper.MapAllowRights(new SavedRightsState(
                    Execute: state.AllowExecute == RightCheckState.Checked,
                    Write: state.AllowWrite == RightCheckState.Checked,
                    Read: true,
                    Special: state.AllowSpecial == RightCheckState.Checked,
                    Own: state.IsAccountOwner == RightCheckState.Checked), isFolder);
                var expectedTrackedRights = effectiveTargetAccessSufficient &&
                                            targetRightsExpansionRequiresPersistence
                    ? GrantRightsMapper.MapAllowRights(existingTargetRights, isFolder)
                    : dbState.TargetFsRights;
                if (diskRights != expectedTrackedRights)
                    trackedGrantNeedsFix = true;
            }

        }

        var traverseCoveragePaths = string.IsNullOrEmpty(dbState.TraverseDir)
            ? []
            : traverseCore.CollectCoveragePaths(dbState.TraverseDir);
        var traversePathsToApply = string.IsNullOrEmpty(dbState.TraverseDir)
            ? []
            : traverseCore.GetPathsNeedingTraverseAce(identitySid, traverseCoveragePaths, unelevated);
        var effectiveTraverseAccessSufficient = string.IsNullOrEmpty(dbState.TraverseDir) ||
                                                traversePathsToApply.Count == 0;
        var targetGrantRequired = !effectiveTargetAccessSufficient || trackedGrantNeedsFix;
        var traverseAceRequired = dbState.TraverseDir != null &&
                                  !effectiveTraverseAccessSufficient;
        var traverseDbRequired = dbState.TraverseDir != null &&
                                 ((persistTargetGrantIntent && targetGrantRequired) ||
                                  traverseAceRequired) &&
                                 !dbState.HasTraverseEntry;

        return new EnsureIdentityPlan(
            Sid: identitySid,
            RequestedRights: requestedRights,
            TargetRights: dbState.TargetRights,
            TargetFsRights: dbState.TargetFsRights,
            TargetRightsExpansionRequiresPersistence: targetRightsExpansionRequiresPersistence,
            EffectiveTargetAccessSufficient: effectiveTargetAccessSufficient,
            TargetGrantRequired: targetGrantRequired,
            TargetGrantDbRequired: targetGrantRequired,
            TargetAceRequired: targetGrantRequired,
            EffectiveTraverseAccessSufficient: effectiveTraverseAccessSufficient,
            TraverseDbRequired: traverseDbRequired,
            TraverseAceRequired: traverseAceRequired,
            TraverseDir: dbState.TraverseDir,
            TraverseCoveragePaths: traverseCoveragePaths,
            TraversePathsToApply: traversePathsToApply,
            DenyConflict: BuildDenyConflictPlan(identitySid, normalized, dbState.DenyEntry, requestedRights, isFolder));
    }

    private DenyConflictPlan? BuildDenyConflictPlan(
        string identitySid,
        string normalized,
        GrantedPathEntry? denyEntry,
        SavedRightsState requestedAllow,
        bool isFolder)
    {
        if (denyEntry == null)
            return null;

        var originalRights = denyEntry.SavedRights ?? SavedRightsState.DefaultForMode(isDeny: true);
        var requestedFsRights = GrantRightsMapper.MapAllowRights(requestedAllow, isFolder);
        var denyFsRights = GrantRightsMapper.MapDenyRights(originalRights, isFolder);
        if ((denyFsRights & requestedFsRights) == 0)
            return null;

        var updatedRights = originalRights with
        {
            Read = originalRights.Read && !requestedAllow.Read,
            Execute = originalRights.Execute && !requestedAllow.Execute
        };

        return new DenyConflictPlan(
            Sid: identitySid,
            Path: normalized,
            OriginalRights: originalRights,
            UpdatedRights: updatedRights,
            RemoveEntry: !updatedRights.Read && !updatedRights.Execute,
            Locations: GrantIntentRepository.FindGrantLocations(identitySid, denyEntry)
                .Where(location => location.Entry.IsDeny)
                .ToList());
    }

    private void ConfirmAccessPlan(EnsureAccessPlan plan, Func<string, string, bool>? confirm)
    {
        foreach (var identity in plan.Identities)
        {
            if (identity.DenyConflict != null)
            {
                if (confirm == null)
                {
                    throw new InvalidOperationException(
                        $"Deny entry blocks requested access on '{plan.Normalized}'. Remove the deny entry first.");
                }

                if (!confirm(plan.Normalized, identity.Sid))
                    throw new GrantAccessDeclinedException($"User declined to resolve deny conflict on '{plan.Normalized}'.");
            }

            if (identity.TargetGrantRequired && confirm != null && !confirm(plan.Normalized, identity.Sid))
                throw new GrantAccessDeclinedException($"User declined to grant access to '{plan.Normalized}'.");
        }
    }

    private List<DenyConflictContext> ApplyPersistedDenyConflicts(EnsureAccessPlan plan)
    {
        var contexts = new List<DenyConflictContext>();

        foreach (var identity in plan.Identities)
        {
            if (identity.DenyConflict is not { } denyConflict)
                continue;

            var runtimeSnapshot = grantRuntimeSnapshotService.CaptureGrantSnapshot(
                denyConflict.Sid,
                denyConflict.Path,
                isDeny: true);
            var locations = denyConflict.Locations.Count > 0
                ? denyConflict.Locations
                : CaptureFallbackDenyLocations(denyConflict.Sid, denyConflict.Path);
            var storeSnapshots = grantIntentMutationStateRestorer.CaptureStoreSnapshots(
                locations.Select(location => (location.Store, OwnerSid: denyConflict.Sid)),
                denyConflict.Path,
                traversePath: null,
                includeDeny: true);
            var context = new DenyConflictContext(denyConflict, runtimeSnapshot, storeSnapshots);

            try
            {
                if (denyConflict.RemoveEntry)
                    fileSystemOperations.RemoveGrant(denyConflict.Sid, denyConflict.Path, isDeny: true, updateFileSystem: true);
                else
                    fileSystemOperations.UpdateGrant(
                        denyConflict.Sid,
                        denyConflict.Path,
                        isDeny: true,
                        denyConflict.UpdatedRights,
                        isFolderOverride: plan.IsFolder);
            }
            catch (Exception ex)
            {
                throw new GrantOperationException(
                    denyConflict.RemoveEntry
                        ? GrantApplyFailureStep.DenyConflictGrantAclRemove
                        : GrantApplyFailureStep.DenyConflictGrantAclApply,
                    denyConflict.Path,
                    grantIntentStoreSaveService.GetPrimaryConfigPath(locations.Select(location => location.Store)),
                    ex);
            }

            foreach (var location in locations)
            {
                if (denyConflict.RemoveEntry)
                {
                    location.Store.RemoveEntry(denyConflict.Sid, location.Entry);
                    continue;
                }

                var replacement = location.Entry.Clone();
                replacement.SavedRights = denyConflict.UpdatedRights;
                location.Store.ReplaceEntry(denyConflict.Sid, location.Entry, replacement);
            }

            try
            {
                grantIntentStoreSaveService.Save(
                    locations.Select(location => location.Store),
                    denyConflict.RemoveEntry
                        ? GrantApplyFailureStep.DenyConflictPostRemoveSave
                        : GrantApplyFailureStep.DenyConflictPostUpdateSave,
                    denyConflict.Path);
            }
            catch (GrantOperationException ex)
            {
                TryRollbackDenyConflicts([.. contexts, context], ex);
                throw;
            }

            contexts.Add(context);
        }

        return contexts;
    }

    private GrantApplyResult ExecutePersistedPlan(
        EnsureAccessPlan plan,
        IReadOnlyList<DenyConflictContext> denyConflictContexts,
        bool unelevated,
        bool persistTargetGrantIntent)
    {
        var contexts = BuildPersistedContexts(plan, persistTargetGrantIntent);
        var modifiedStores = ApplyIntentMutations(contexts, plan);
        if (modifiedStores.Count > 0)
        {
            try
            {
                grantIntentStoreSaveService.Save(modifiedStores, GrantApplyFailureStep.GrantIntentSave, plan.Normalized);
            }
            catch (GrantOperationException ex)
            {
                TryRollbackPreCompletionSaveFailure(plan, contexts, denyConflictContexts, ex);
                throw;
            }
        }

        var temporaryTargetGrantSnapshots = persistTargetGrantIntent
            ? []
            : contexts
                .Where(context =>
                    context.Plan.TargetAceRequired &&
                    context.TargetSelection.Stores.Count == 0 &&
                    context.GrantSnapshot.Entry == null)
                .Select(context => context.GrantSnapshot)
                .ToList();

        var appliedTraverseBySid = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var appliedTargetSids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var primaryConfigPath = grantIntentStoreSaveService.GetPrimaryConfigPath(modifiedStores);

        try
        {
            foreach (var context in contexts)
            {
                if (context.Plan.TraverseAceRequired && context.Plan.TraversePathsToApply.Count > 0)
                {
                    var applied = traverseCore.ApplyTraverseAces(context.Plan.Sid, context.Plan.TraversePathsToApply).ToList();
                    appliedTraverseBySid[context.Plan.Sid] = applied;
                    if (applied.Count > 0)
                        traverseCore.VerifyEffectiveTraverse(context.Plan.Sid, context.Plan.TraverseCoveragePaths, unelevated);
                }

                if (!context.Plan.TargetAceRequired)
                    continue;

                fileSystemOperations.AddGrant(
                    context.Plan.Sid,
                    plan.Normalized,
                    isDeny: false,
                    context.Plan.TargetRights,
                    isFolderOverride: plan.IsFolder);
                appliedTargetSids.Add(context.Plan.Sid);
                ValidateEffectiveAccess(plan.Normalized, context.Plan, unelevated);
            }

            if (temporaryTargetGrantSnapshots.Count > 0)
            {
                dbAccessor.Write(db =>
                {
                    foreach (var snapshot in temporaryTargetGrantSnapshots)
                    {
                        var grants = db.GetAccount(snapshot.Sid)?.Grants;
                        if (grants == null)
                            continue;

                        var currentGrant = GrantEntryLookup.FindGrantEntryInList(
                            grants,
                            snapshot.Path,
                            isDeny: false);
                        if (currentGrant != null)
                            grants.Remove(currentGrant);

                        db.RemoveAccountIfEmpty(snapshot.Sid);
                    }
                });
            }
        }
        catch (Exception ex)
        {
            var operationException = new GrantOperationException(
                ResolveFailureStep(ex),
                plan.Normalized,
                primaryConfigPath,
                ex is TraverseAclApplyException traverseApplyException
                    ? traverseApplyException.InnerException ?? traverseApplyException
                    : ex);

            TryRollbackAppliedAccess(
                plan,
                contexts,
                appliedTraverseBySid,
                appliedTargetSids,
                operationException);
            TryRollbackDenyConflicts(denyConflictContexts, operationException);
            throw operationException;
        }

        return new GrantApplyResult(
            GrantApplied: appliedTargetSids.Count > 0,
            TraverseApplied: appliedTraverseBySid.Values.Any(paths => paths.Count > 0),
            DatabaseModified: modifiedStores.Count > 0,
            DurableSaveCompleted: modifiedStores.Count > 0);
    }

    private List<PersistedIdentityContext> BuildPersistedContexts(EnsureAccessPlan plan, bool persistTargetGrantIntent)
    {
        var contexts = new List<PersistedIdentityContext>();

        foreach (var identity in plan.Identities)
        {
            if (!identity.TargetAceRequired && !identity.TraverseAceRequired && !identity.TraverseDbRequired)
                continue;

            var targetSelection = persistTargetGrantIntent && identity.TargetGrantDbRequired
                ? ResolveTargetStores(plan.Normalized, identity)
                : new StoreSelection(identity.Sid, []);
            var traverseSelection = ResolveTraverseStores(identity);
            var storeKeys = new List<(IGrantIntentStore Store, string OwnerSid)>();
            if (persistTargetGrantIntent && identity.TargetGrantDbRequired)
                storeKeys.AddRange(targetSelection.Stores.Select(store => (store, targetSelection.OwnerSid)));
            if (traverseSelection != null)
                storeKeys.AddRange(traverseSelection.Value.Stores.Select(store => (store, traverseSelection.Value.OwnerSid)));

            contexts.Add(new PersistedIdentityContext(
                identity,
                targetSelection,
                traverseSelection,
                grantRuntimeSnapshotService.CaptureGrantSnapshot(identity.Sid, plan.Normalized, isDeny: false),
                identity.TraverseDbRequired && !string.IsNullOrEmpty(identity.TraverseDir)
                    ? grantRuntimeSnapshotService.CaptureTraverseSnapshot(identity.Sid, identity.TraverseDir)
                    : null,
                grantIntentMutationStateRestorer.CaptureStoreSnapshots(
                    storeKeys,
                    plan.Normalized,
                    identity.TraverseDir,
                    includeDeny: false)));
        }

        return contexts;
    }

    private HashSet<IGrantIntentStore> ApplyIntentMutations(IReadOnlyList<PersistedIdentityContext> contexts, EnsureAccessPlan plan)
    {
        var modifiedStores = new HashSet<IGrantIntentStore>();

        foreach (var context in contexts)
        {
            if (context.Plan.TargetGrantDbRequired && context.TargetSelection.Stores.Count > 0)
            {
                var targetEntry = BuildTargetEntry(plan, context.Plan, context.GrantSnapshot.Entry);
                foreach (var store in context.TargetSelection.Stores)
                {
                    if (UpsertStoreEntry(store, context.TargetSelection.OwnerSid, targetEntry))
                        modifiedStores.Add(store);
                }
            }

            if (context.Plan.TraverseDbRequired &&
                context.TraverseSelection is { } traverseSelection &&
                !string.IsNullOrEmpty(context.Plan.TraverseDir))
            {
                var traverseEntry = BuildTraverseEntry(plan, context.Plan, context.TraverseSnapshot?.Entry);
                foreach (var store in traverseSelection.Stores)
                {
                    if (UpsertStoreEntry(store, traverseSelection.OwnerSid, traverseEntry))
                        modifiedStores.Add(store);
                }
            }

            ApplyRuntimeIntentMutation(plan, context.Plan, includeTargetGrant: context.Plan.TargetGrantDbRequired && context.TargetSelection.Stores.Count > 0);
        }

        return modifiedStores;
    }

    private void ApplyRuntimeIntentMutation(EnsureAccessPlan plan, EnsureIdentityPlan identity, bool includeTargetGrant)
    {
        dbAccessor.Write(db =>
        {
            if (includeTargetGrant)
            {
                var existingGrant = GrantEntryLookup.FindGrantEntryInDb(db, identity.Sid, plan.Normalized, isDeny: false);
                if (existingGrant == null)
                {
                    db.GetOrCreateAccount(identity.Sid).Grants.Add(BuildTargetEntry(plan, identity, existingEntry: null));
                }
                else
                {
                    existingGrant.SavedRights = identity.TargetRights;
                    MergeManagedSourceTracking(existingGrant, ResolveSourceContainerSid(plan, identity.Sid), entryWasCreated: false);
                }
            }

            if (identity.TraverseDbRequired && !string.IsNullOrEmpty(identity.TraverseDir))
            {
                var existingTraverse = FindRuntimeTraverseEntry(db, identity.Sid, identity.TraverseDir);
                if (existingTraverse == null)
                {
                    traverseGrantOwnerResolver.GetOrCreateTraverseStore(db, identity.Sid)
                        .Add(BuildTraverseEntry(plan, identity, existingEntry: null));
                }
                else
                {
                    existingTraverse.AllAppliedPaths = identity.TraverseCoveragePaths.ToList();
                    MergeManagedSourceTracking(
                        existingTraverse,
                        ResolveSourceContainerSid(plan, identity.Sid) ?? (AclHelper.IsSpecificContainerSid(identity.Sid) ? identity.Sid : null),
                        entryWasCreated: false);
                }
            }
        });
    }

    private StoreSelection ResolveTargetStores(string normalizedPath, EnsureIdentityPlan identity)
    {
        var existingLocations = GrantIntentRepository.FindEntriesForSid(identity.Sid)
            .Where(location =>
                !location.Entry.IsTraverseOnly &&
                !location.Entry.IsDeny &&
                string.Equals(location.Entry.Path, normalizedPath, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (existingLocations.Count > 0)
        {
            return new StoreSelection(identity.Sid, existingLocations
                .Select(location => location.Store)
                .Distinct()
                .ToList());
        }

        return new StoreSelection(identity.Sid, [MainGrantIntentStore]);
    }

    private StoreSelection? ResolveTraverseStores(EnsureIdentityPlan identity)
    {
        if (!identity.TraverseDbRequired || string.IsNullOrEmpty(identity.TraverseDir))
            return null;

        var ownerSid = TraverseEntryLookup.ResolveStorageOwnerSid(identity.Sid);
        var existingLocations = GrantIntentRepository.FindEntriesForSid(ownerSid)
            .Where(location =>
                location.Entry.IsTraverseOnly &&
                string.Equals(location.Entry.Path, identity.TraverseDir, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (existingLocations.Count > 0)
        {
            return new StoreSelection(ownerSid, existingLocations
                .Select(location => location.Store)
                .Distinct()
                .ToList());
        }

        return new StoreSelection(ownerSid, [MainGrantIntentStore]);
    }

    private IReadOnlyList<GrantIntentLocation> CaptureFallbackDenyLocations(string sid, string path)
    {
        var normalizedPath = Path.GetFullPath(path);
        var existing = dbAccessor.Read(db => GrantEntryLookup.FindGrantEntryInDb(db, sid, normalizedPath, isDeny: true)?.Clone());
        if (existing == null)
            return [];

        return [new GrantIntentLocation(existing, MainGrantIntentStore)];
    }

    private bool UpsertStoreEntry(IGrantIntentStore store, string ownerSid, GrantedPathEntry entry)
    {
        var existing = store.GetEntries(ownerSid)
            .FirstOrDefault(candidate =>
                string.Equals(candidate.Path, entry.Path, StringComparison.OrdinalIgnoreCase) &&
                candidate.IsDeny == entry.IsDeny &&
                candidate.IsTraverseOnly == entry.IsTraverseOnly);
        if (existing == null)
        {
            store.AddEntry(ownerSid, entry);
            return true;
        }

        var mergedEntry = entry.Clone();
        if (existing.SourceSids == null)
        {
            mergedEntry.SourceSids = null;
        }
        else
        {
            mergedEntry.SourceSids = existing.SourceSids.ToList();
            if (entry.SourceSids != null)
            {
                foreach (var sourceSid in entry.SourceSids)
                {
                    if (!mergedEntry.SourceSids.Contains(sourceSid, StringComparer.OrdinalIgnoreCase))
                        mergedEntry.SourceSids.Add(sourceSid);
                }
            }
        }

        if (EntriesEquivalent(existing, mergedEntry))
            return false;

        return store.ReplaceEntry(ownerSid, existing, mergedEntry);
    }

    private void TryRollbackAppliedAccess(
        EnsureAccessPlan plan,
        IReadOnlyList<PersistedIdentityContext> contexts,
        IReadOnlyDictionary<string, List<string>> appliedTraverseBySid,
        IReadOnlySet<string> appliedTargetSids,
        GrantOperationException operationException)
    {
        foreach (var context in contexts)
        {
            if (appliedTargetSids.Contains(context.Plan.Sid))
            {
                try
                {
                    var previousGrant = context.GrantSnapshot.Entry;
                    if (previousGrant == null)
                    {
                        fileSystemOperations.RemoveGrant(context.Plan.Sid, plan.Normalized, isDeny: false, updateFileSystem: true);
                    }
                    else
                    {
                        fileSystemOperations.UpdateGrant(
                            context.Plan.Sid,
                            plan.Normalized,
                            isDeny: false,
                            previousGrant.SavedRights ?? SavedRightsState.DefaultForMode(isDeny: false),
                            isFolderOverride: plan.IsFolder,
                            desiredPreviousSaclLabel: previousGrant.PreviousSaclLabel);
                    }
                }
                catch (Exception ex)
                {
                    operationException.AppendCleanupFailure(
                        GrantApplyFailureStep.GrantAclRollback,
                        plan.Normalized,
                        null,
                        ex);
                }
            }

            if (appliedTraverseBySid.TryGetValue(context.Plan.Sid, out var appliedTraverse) && appliedTraverse.Count > 0)
            {
                if (operationException.Step == GrantApplyFailureStep.TraverseAclApply)
                    continue;

                try
                {
                    traverseCore.RemoveTraverseAces(context.Plan.Sid, appliedTraverse);
                }
                catch (Exception ex)
                {
                    operationException.AppendCleanupFailure(
                        GrantApplyFailureStep.TraverseAclRollback,
                        appliedTraverse[0],
                        null,
                        ex);
                }
            }
        }

        if (TryRestoreRuntimeSnapshots(contexts, plan.Normalized, operationException))
        {
            grantIntentMutationStateRestorer.TryRestoreStoreSnapshots(
                contexts.SelectMany(context => context.StoreSnapshots).ToList(),
                plan.Normalized,
                operationException);
        }
    }

    private void TryRollbackPreCompletionSaveFailure(
        EnsureAccessPlan plan,
        IReadOnlyList<PersistedIdentityContext> contexts,
        IReadOnlyList<DenyConflictContext> denyConflictContexts,
        GrantOperationException operationException)
    {
        if (TryRestoreRuntimeSnapshots(contexts, plan.Normalized, operationException))
        {
            grantIntentMutationStateRestorer.TryRestoreStoreSnapshots(
                contexts.SelectMany(context => context.StoreSnapshots).ToList(),
                plan.Normalized,
                operationException);
        }

        TryRollbackDenyConflicts(denyConflictContexts, operationException);
    }

    private void TryRollbackDenyConflicts(
        IReadOnlyList<DenyConflictContext> contexts,
        GrantOperationException operationException)
    {
        foreach (var context in contexts.Reverse())
        {
            try
            {
                if (context.Plan.RemoveEntry)
                {
                    fileSystemOperations.AddGrant(
                        context.Plan.Sid,
                        context.Plan.Path,
                        isDeny: true,
                        context.Plan.OriginalRights);
                }
                else
                {
                    fileSystemOperations.UpdateGrant(
                        context.Plan.Sid,
                        context.Plan.Path,
                        isDeny: true,
                        context.Plan.OriginalRights);
                }

                grantRuntimeSnapshotService.RestoreGrantSnapshot(context.RuntimeSnapshot);
                var rollbackException = new GrantOperationException(
                    GrantApplyFailureStep.DenyConflictRollback,
                    context.Plan.Path,
                    grantIntentStoreSaveService.GetPrimaryConfigPath(context.StoreSnapshots.Select(snapshot => snapshot.Store)),
                    new InvalidOperationException("Deny-conflict rollback failed."));
                grantIntentMutationStateRestorer.TryRestoreStoreSnapshots(
                    context.StoreSnapshots,
                    context.Plan.Path,
                    rollbackException);
                operationException.AppendCleanupFailures(rollbackException.CleanupFailures);
            }
            catch (GrantOperationException ex)
            {
                operationException.AppendCleanupFailure(
                    GrantApplyFailureStep.DenyConflictRollback,
                    context.Plan.Path,
                    ex.ConfigPath,
                    ex.Cause);
            }
            catch (Exception ex)
            {
                operationException.AppendCleanupFailure(
                    GrantApplyFailureStep.DenyConflictRollback,
                    context.Plan.Path,
                    null,
                    ex);
            }
        }
    }

    private bool TryRestoreRuntimeSnapshots(
        IReadOnlyList<PersistedIdentityContext> contexts,
        string normalizedPath,
        GrantOperationException operationException)
    {
        try
        {
            foreach (var context in contexts)
            {
                grantRuntimeSnapshotService.RestoreGrantSnapshot(context.GrantSnapshot);
                if (context.TraverseSnapshot != null)
                    grantRuntimeSnapshotService.RestoreTraverseSnapshot(context.TraverseSnapshot);
            }

            return true;
        }
        catch (Exception ex)
        {
            operationException.AppendCleanupFailure(
                GrantApplyFailureStep.RevertIntentSave,
                normalizedPath,
                null,
                ex);
            return false;
        }
    }

    private static GrantApplyFailureStep ResolveFailureStep(Exception ex)
        => ex switch
        {
            TraverseAclApplyException => GrantApplyFailureStep.TraverseAclApply,
            InvalidOperationException invalidOperation when invalidOperation.Message.Contains("Traverse access is still insufficient", StringComparison.Ordinal) =>
                GrantApplyFailureStep.TraverseEffectiveAccessValidation,
            InvalidOperationException invalidOperation when invalidOperation.Message.Contains("Grant applied but effective access is still insufficient", StringComparison.Ordinal) =>
                GrantApplyFailureStep.TargetEffectiveAccessValidation,
            _ => GrantApplyFailureStep.GrantAclApply
        };

    private void ValidateEffectiveAccess(string normalizedPath, EnsureIdentityPlan identity, bool unelevated)
    {
        if (aclPermission.NeedsPermissionGrant(normalizedPath, identity.Sid, identity.TargetFsRights, unelevated))
        {
            throw new InvalidOperationException(
                $"Grant applied but effective access is still insufficient on '{normalizedPath}'. " +
                $"A parent deny entry may be blocking access.");
        }
    }

    private IReadOnlyList<string> ResolveIdentitySids(string requestedSid)
    {
        if (!AclHelper.IsSpecificContainerSid(requestedSid))
            return [requestedSid];

        var interactiveSid = interactiveUserResolver.GetInteractiveUserSid();
        if (string.IsNullOrEmpty(interactiveSid) ||
            string.Equals(interactiveSid, requestedSid, StringComparison.OrdinalIgnoreCase))
        {
            return [requestedSid];
        }

        return [requestedSid, interactiveSid];
    }

    private static GrantedPathEntry BuildTargetEntry(EnsureAccessPlan plan, EnsureIdentityPlan identity, GrantedPathEntry? existingEntry)
    {
        var entry = existingEntry?.Clone() ?? new GrantedPathEntry();
        entry.Path = plan.Normalized;
        entry.IsDeny = false;
        entry.IsTraverseOnly = false;
        entry.SavedRights = identity.TargetRights;
        MergeManagedSourceTracking(entry, ResolveSourceContainerSid(plan, identity.Sid), entryWasCreated: existingEntry == null);
        return entry;
    }

    private static GrantedPathEntry BuildTraverseEntry(EnsureAccessPlan plan, EnsureIdentityPlan identity, GrantedPathEntry? existingEntry)
    {
        var entry = existingEntry?.Clone() ?? new GrantedPathEntry();
        entry.Path = identity.TraverseDir!;
        entry.IsTraverseOnly = true;
        entry.IsDeny = false;
        entry.SavedRights = null;
        entry.AllAppliedPaths = identity.TraverseCoveragePaths.ToList();
        MergeManagedSourceTracking(
            entry,
            ResolveSourceContainerSid(plan, identity.Sid) ?? (AclHelper.IsSpecificContainerSid(identity.Sid) ? identity.Sid : null),
            entryWasCreated: existingEntry == null);
        return entry;
    }

    private static void MergeManagedSourceTracking(GrantedPathEntry entry, string? sourceSid, bool entryWasCreated)
    {
        if (string.IsNullOrEmpty(sourceSid))
            return;

        if (entry.SourceSids == null && !entryWasCreated)
            return;

        entry.SourceSids ??= [];
        if (!entry.SourceSids.Contains(sourceSid, StringComparer.OrdinalIgnoreCase))
            entry.SourceSids.Add(sourceSid);
    }

    private static string? ResolveSourceContainerSid(EnsureAccessPlan plan, string identitySid)
    {
        if (string.IsNullOrEmpty(plan.RequestedContainerSid) ||
            string.Equals(plan.RequestedContainerSid, identitySid, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return plan.RequestedContainerSid;
    }

    private GrantedPathEntry? FindRuntimeTraverseEntry(AppDatabase database, string sid, string path)
        => grantRuntimeSnapshotService.FindTraverseEntry(
            database,
            sid,
            TraverseEntryLookup.NormalizePathForLookup(path),
            includeManualSharedEntries: false);

    private static bool EntriesEquivalent(GrantedPathEntry left, GrantedPathEntry right)
    {
        return string.Equals(left.Path, right.Path, StringComparison.OrdinalIgnoreCase) &&
               left.IsDeny == right.IsDeny &&
               left.IsTraverseOnly == right.IsTraverseOnly &&
               Equals(left.SavedRights, right.SavedRights) &&
               SequenceEqual(left.AllAppliedPaths, right.AllAppliedPaths) &&
               SequenceEqual(left.SourceSids, right.SourceSids);
    }

    private static bool SequenceEqual(IReadOnlyList<string>? left, IReadOnlyList<string>? right)
    {
        if (left == null || right == null)
            return left == right;

        return left.SequenceEqual(right, StringComparer.OrdinalIgnoreCase);
    }

    private (bool IsFolder, bool PathExists) ResolvePathState(string normalized)
    {
        if (aclAccessor.PathExists(normalized, out var isFolder))
            return (isFolder, true);

        isFolder = pathInfo.DirectoryExists(normalized);
        return (isFolder, isFolder || pathInfo.FileExists(normalized));
    }
}
