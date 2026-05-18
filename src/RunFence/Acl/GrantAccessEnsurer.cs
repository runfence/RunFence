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
    IAclAccessor aclAccessor,
    IFileSystemPathInfo pathInfo,
    ITraverseCoreOperations traverseCore,
    GrantFileSystemOperations fileSystemOperations,
    IInteractiveUserResolver interactiveUserResolver,
    ITraverseGrantOwnerResolver traverseGrantOwnerResolver,
    Func<IGrantIntentRepository> grantIntentRepository,
    Func<IGrantIntentStore> mainGrantIntentStore,
    IGrantIntentStoreSaveService grantIntentStoreSaveService)
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

    private readonly record struct RuntimeStateSnapshot(
        string Sid,
        string NormalizedPath,
        bool IsDeny,
        GrantedPathEntry? GrantEntry,
        string? TraversePath,
        GrantedPathEntry? TraverseEntry);

    private readonly record struct StoreEntrySnapshot(
        IGrantIntentStore Store,
        string OwnerSid,
        string TargetPath,
        string? TraversePath,
        bool IncludeDeny,
        IReadOnlyList<GrantedPathEntry> Entries);

    private readonly record struct PersistedIdentityContext(
        EnsureIdentityPlan Plan,
        StoreSelection TargetSelection,
        StoreSelection? TraverseSelection,
        RuntimeStateSnapshot RuntimeSnapshot,
        IReadOnlyList<StoreEntrySnapshot> StoreSnapshots);

    private readonly record struct DenyConflictContext(
        DenyConflictPlan Plan,
        RuntimeStateSnapshot RuntimeSnapshot,
        IReadOnlyList<StoreEntrySnapshot> StoreSnapshots);

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
                    traverseGrantOwnerResolver.ResolveAclSid(identitySid),
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
            var existing = GrantCoreOperations.FindGrantEntryInDb(db, identitySid, normalized, isDeny: false);
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
            var denyEntry = GrantCoreOperations.FindGrantEntryInDb(db, identitySid, normalized, isDeny: true);

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
        var trackedTraverseNeedsFix = false;

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

            if (!string.IsNullOrEmpty(dbState.TraverseDir) &&
                dbState.HasTraverseEntry &&
                !TraverseRightsHelper.HasEffectiveTraverseForGrantSid(
                    dbState.TraverseDir,
                    identitySid,
                    groupSids,
                    aclPermission,
                    pathInfo))
            {
                trackedTraverseNeedsFix = true;
            }
        }

        var traverseCoveragePaths = string.IsNullOrEmpty(dbState.TraverseDir)
            ? []
            : traverseCore.CollectCoveragePaths(dbState.TraverseDir);
        var traversePathsToApply = string.IsNullOrEmpty(dbState.TraverseDir)
            ? []
            : traverseCore.GetPathsNeedingTraverseAce(identitySid, traverseCoveragePaths);
        var effectiveTraverseAccessSufficient = string.IsNullOrEmpty(dbState.TraverseDir) ||
                                                traversePathsToApply.Count == 0;
        var targetGrantRequired = !effectiveTargetAccessSufficient || trackedGrantNeedsFix;
        var traverseRequired = dbState.TraverseDir != null &&
                               (targetGrantRequired || dbState.HasTraverseEntry) &&
                               !effectiveTraverseAccessSufficient;
        var traverseDbRequired = traverseRequired &&
                                 (!dbState.HasTraverseEntry || trackedTraverseNeedsFix);
        var traverseAceRequired = traverseRequired;

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
                    throw new OperationCanceledException($"User declined to resolve deny conflict on '{plan.Normalized}'.");
            }

            if (identity.TargetGrantRequired && confirm != null && !confirm(plan.Normalized, identity.Sid))
                throw new OperationCanceledException($"User declined to grant access to '{plan.Normalized}'.");
        }
    }

    private List<DenyConflictContext> ApplyPersistedDenyConflicts(EnsureAccessPlan plan)
    {
        var contexts = new List<DenyConflictContext>();

        foreach (var identity in plan.Identities)
        {
            if (identity.DenyConflict is not { } denyConflict)
                continue;

            var runtimeSnapshot = CaptureRuntimeSnapshot(denyConflict.Sid, denyConflict.Path, includeTraverse: false, isDeny: true);
            var locations = denyConflict.Locations.Count > 0
                ? denyConflict.Locations
                : CaptureFallbackDenyLocations(denyConflict.Sid, denyConflict.Path);
            var storeSnapshots = CaptureStoreSnapshots(
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
                TryRollbackPreCompletionSaveFailure(plan, contexts, denyConflictContexts, modifiedStores, ex);
                throw;
            }
        }

        var temporaryTargetGrantSnapshots = persistTargetGrantIntent
            ? []
            : contexts
                .Where(context =>
                    context.Plan.TargetAceRequired &&
                    context.TargetSelection.Stores.Count == 0 &&
                    context.RuntimeSnapshot.GrantEntry == null)
                .Select(context => context.RuntimeSnapshot)
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
                        traverseCore.VerifyEffectiveTraverse(context.Plan.Sid, context.Plan.TraverseCoveragePaths);
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

                        var currentGrant = GrantCoreOperations.FindGrantEntryInList(
                            grants,
                            snapshot.NormalizedPath,
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
                modifiedStores,
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
            if (!identity.TargetAceRequired && !identity.TraverseAceRequired)
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
                CaptureRuntimeSnapshot(identity.Sid, plan.Normalized, includeTraverse: true, isDeny: false),
                CaptureStoreSnapshots(storeKeys, plan.Normalized, identity.TraverseDir, includeDeny: false)));
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
                var targetEntry = BuildTargetEntry(plan, context.Plan, context.RuntimeSnapshot.GrantEntry);
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
                var traverseEntry = BuildTraverseEntry(plan, context.Plan, context.RuntimeSnapshot.TraverseEntry);
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
                var existingGrant = GrantCoreOperations.FindGrantEntryInDb(db, identity.Sid, plan.Normalized, isDeny: false);
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

        var ownerSid = traverseGrantOwnerResolver.ResolveStorageOwnerSid(identity.Sid);
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

    private RuntimeStateSnapshot CaptureRuntimeSnapshot(string sid, string normalizedPath, bool includeTraverse, bool isDeny)
    {
        return dbAccessor.Read(db =>
        {
            var grantEntry = GrantCoreOperations.FindGrantEntryInDb(db, sid, normalizedPath, isDeny)?.Clone();
            string? traversePath = null;
            GrantedPathEntry? traverseEntry = null;
            if (includeTraverse)
            {
                traversePath = pathInfo.DirectoryExists(normalizedPath)
                    ? normalizedPath
                    : Path.GetDirectoryName(normalizedPath);
                if (!string.IsNullOrEmpty(traversePath))
                    traverseEntry = FindRuntimeTraverseEntry(db, sid, traversePath)?.Clone();
            }

            return new RuntimeStateSnapshot(sid, normalizedPath, isDeny, grantEntry, traversePath, traverseEntry);
        });
    }

    private List<StoreEntrySnapshot> CaptureStoreSnapshots(
        IEnumerable<(IGrantIntentStore Store, string OwnerSid)> storeKeys,
        string normalizedPath,
        string? traversePath,
        bool includeDeny)
    {
        var result = new List<StoreEntrySnapshot>();
        foreach (var group in storeKeys
                     .Distinct()
                     .GroupBy(key => new { key.Store, key.OwnerSid }))
        {
            var entries = group.Key.Store.GetEntries(group.Key.OwnerSid)
                .Where(entry =>
                    string.Equals(entry.Path, normalizedPath, StringComparison.OrdinalIgnoreCase) ||
                    (entry.IsTraverseOnly &&
                     !string.IsNullOrEmpty(traversePath) &&
                     string.Equals(entry.Path, traversePath, StringComparison.OrdinalIgnoreCase)))
                .Where(entry => includeDeny || !entry.IsDeny || entry.IsTraverseOnly)
                .Select(entry => entry.Clone())
                .ToList();
            result.Add(new StoreEntrySnapshot(
                group.Key.Store,
                group.Key.OwnerSid,
                normalizedPath,
                traversePath,
                includeDeny,
                entries));
        }

        return result;
    }

    private IReadOnlyList<GrantIntentLocation> CaptureFallbackDenyLocations(string sid, string path)
    {
        var normalizedPath = Path.GetFullPath(path);
        var existing = dbAccessor.Read(db => GrantCoreOperations.FindGrantEntryInDb(db, sid, normalizedPath, isDeny: true)?.Clone());
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
        IEnumerable<IGrantIntentStore> modifiedStores,
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
                    var previousGrant = context.RuntimeSnapshot.GrantEntry;
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

        try
        {
            RestoreRuntimeSnapshots(contexts.Select(context => context.RuntimeSnapshot));
            RestoreStoreSnapshots(contexts.SelectMany(context => context.StoreSnapshots), plan.Normalized);
            grantIntentStoreSaveService.Save(modifiedStores, GrantApplyFailureStep.RevertIntentSave, plan.Normalized);
        }
        catch (GrantOperationException ex)
        {
            operationException.AppendCleanupFailures(ex.CleanupFailures);
            operationException.AppendCleanupFailure(GrantApplyFailureStep.RevertIntentSave, plan.Normalized, ex.ConfigPath, ex.Cause);
        }
        catch (Exception ex)
        {
            operationException.AppendCleanupFailure(GrantApplyFailureStep.RevertIntentSave, plan.Normalized, null, ex);
        }
    }

    private void TryRollbackPreCompletionSaveFailure(
        EnsureAccessPlan plan,
        IReadOnlyList<PersistedIdentityContext> contexts,
        IReadOnlyList<DenyConflictContext> denyConflictContexts,
        IEnumerable<IGrantIntentStore> modifiedStores,
        GrantOperationException operationException)
    {
        try
        {
            RestoreRuntimeSnapshots(contexts.Select(context => context.RuntimeSnapshot));
            RestoreStoreSnapshots(contexts.SelectMany(context => context.StoreSnapshots), plan.Normalized);
            grantIntentStoreSaveService.Save(modifiedStores, GrantApplyFailureStep.RevertIntentSave, plan.Normalized);
        }
        catch (GrantOperationException ex)
        {
            operationException.AppendCleanupFailures(ex.CleanupFailures);
            operationException.AppendCleanupFailure(GrantApplyFailureStep.RevertIntentSave, plan.Normalized, ex.ConfigPath, ex.Cause);
        }
        catch (Exception ex)
        {
            operationException.AppendCleanupFailure(
                GrantApplyFailureStep.RevertIntentSave,
                plan.Normalized,
                grantIntentStoreSaveService.GetPrimaryConfigPath(modifiedStores),
                ex);
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

                RestoreRuntimeSnapshots([context.RuntimeSnapshot]);
                RestoreStoreSnapshots(context.StoreSnapshots, context.Plan.Path);
                grantIntentStoreSaveService.Save(
                    context.StoreSnapshots.Select(snapshot => snapshot.Store),
                    GrantApplyFailureStep.DenyConflictRollback,
                    context.Plan.Path);
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

    private void RestoreRuntimeSnapshots(IEnumerable<RuntimeStateSnapshot> snapshots)
    {
        dbAccessor.Write(db =>
        {
            foreach (var snapshot in snapshots)
            {
                var account = db.GetAccount(snapshot.Sid);
                var grants = account?.Grants;
                if (grants != null)
                {
                    var currentGrant = GrantCoreOperations.FindGrantEntryInList(grants, snapshot.NormalizedPath, snapshot.IsDeny);
                    if (currentGrant != null)
                        grants.Remove(currentGrant);
                    if (snapshot.GrantEntry != null)
                        db.GetOrCreateAccount(snapshot.Sid).Grants.Add(snapshot.GrantEntry.Clone());
                }
                else if (snapshot.GrantEntry != null)
                {
                    db.GetOrCreateAccount(snapshot.Sid).Grants.Add(snapshot.GrantEntry.Clone());
                }

                if (!string.IsNullOrEmpty(snapshot.TraversePath))
                {
                    var traverseEntries = traverseGrantOwnerResolver.GetOrCreateTraverseStore(db, snapshot.Sid);
                    var currentTraverse = traverseGrantOwnerResolver.FindTraverseEntry(
                        db,
                        snapshot.Sid,
                        snapshot.TraversePath);
                    if (currentTraverse != null)
                        traverseEntries.Remove(currentTraverse);
                    if (snapshot.TraverseEntry != null)
                        traverseEntries.Add(snapshot.TraverseEntry.Clone());
                }

                db.RemoveAccountIfEmpty(snapshot.Sid);
            }
        });
    }

    private void RestoreStoreSnapshots(IEnumerable<StoreEntrySnapshot> snapshots, string normalizedPath)
    {
        foreach (var snapshot in snapshots
                     .DistinctBy(snapshot => new { snapshot.Store, snapshot.OwnerSid }))
        {
            var current = snapshot.Store.GetEntries(snapshot.OwnerSid)
                .Where(entry =>
                    string.Equals(entry.Path, snapshot.TargetPath, StringComparison.OrdinalIgnoreCase) ||
                    (entry.IsTraverseOnly &&
                     !string.IsNullOrEmpty(snapshot.TraversePath) &&
                     string.Equals(entry.Path, snapshot.TraversePath, StringComparison.OrdinalIgnoreCase)))
                .Where(entry => snapshot.IncludeDeny || !entry.IsDeny || entry.IsTraverseOnly)
                .ToList();
            foreach (var entry in current)
                snapshot.Store.RemoveEntry(snapshot.OwnerSid, entry);
            foreach (var entry in snapshot.Entries)
                snapshot.Store.AddEntry(snapshot.OwnerSid, entry);
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
        => traverseGrantOwnerResolver.FindTraverseEntry(database, sid, path);

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
