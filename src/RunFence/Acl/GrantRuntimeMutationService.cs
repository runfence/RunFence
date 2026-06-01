using RunFence.Acl.Permissions;
using RunFence.Acl.Traverse;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Persistence;

namespace RunFence.Acl;

public class GrantRuntimeMutationService(
    ITraverseCoreOperations traverseCore,
    UiThreadDatabaseAccessor dbAccessor,
    ContainerInteractiveUserSync containerIuSync,
    LowIntegrityGrantSync lowIntegrityGrantSync,
    IMandatoryLabelService mandatoryLabelService,
    GrantFileSystemOperations fileSystemOperations,
    IGrantAceService grantAceService,
    IFileSystemPathInfo pathInfo,
    TraverseGrantStateService traverseGrantStateService)
{
    public GrantOperationResult ApplyTrackedGrantAcl(
        string sid,
        string path,
        bool isDeny,
        SavedRightsState savedRights,
        string? ownerSid = null)
        => ApplyGrantCore(
            sid,
            path,
            isDeny,
            savedRights,
            ownerSid,
            skipAllowSideEffectsWhenGrantAlreadyTracked: false);

    public GrantOperationResult ApplyGrantCore(
        string sid,
        string path,
        bool isDeny,
        SavedRightsState rights,
        string? ownerSid,
        bool skipAllowSideEffectsWhenGrantAlreadyTracked)
    {
        var normalized = Path.GetFullPath(path);
        var operationResult = fileSystemOperations.AddGrant(sid, normalized, isDeny, rights, ownerSid);

        if (isDeny || (skipAllowSideEffectsWhenGrantAlreadyTracked && !operationResult.GrantAdded))
            return operationResult;

        var traverseAdded = false;
        var traverseDir = pathInfo.DirectoryExists(normalized)
            ? normalized
            : Path.GetDirectoryName(normalized);
        if (!string.IsNullOrEmpty(traverseDir))
        {
            var (modified, visitedPaths) = traverseCore.AddTraverse(sid, traverseDir);
            traverseAdded = modified || visitedPaths.Count > 0;
        }

        if (AclHelper.IsContainerSid(sid))
            containerIuSync.SyncAllowGrantToInteractiveUser(sid, normalized, rights);
        else if (AclHelper.IsLowIntegritySid(sid))
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
                    var entry = GrantEntryLookup.FindGrantEntryInDb(db, sid, normalized, isDeny: false);
                    if (entry != null)
                        entry.SourceSids = sources;
                });
            }
        }

        if (!AclHelper.IsLowIntegritySid(sid) && !AclHelper.IsContainerSid(sid))
        {
            dbAccessor.Write(db =>
            {
                var lowIlEntry = GrantEntryLookup.FindGrantEntryInDb(
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

    public bool RemoveGrantAclOnly(
        string sid,
        string path,
        bool isDeny,
        SavedRightsState? savedRights = null,
        string? previousSaclLabel = null)
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
        {
            lowIntegrityGrantSync.RevertSource(sid, normalized, updateFileSystem: true);
        }

        return true;
    }

    public bool RemoveGrantWithoutPersisting(string sid, string path, bool isDeny, bool updateFileSystem)
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

    public void ApplyRuntimeGrantChange(
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

    public void RemoveRuntimeGrant(string sid, string path, GrantedPathEntry entry, bool updateFileSystem)
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

    public void RemoveTrackedGrantWithoutFilesystem(
        string sid,
        string normalizedPath,
        GrantedPathEntry entry)
    {
        RemoveRuntimeGrant(sid, normalizedPath, entry, updateFileSystem: false);
    }

    public void CleanupDerivedGrantSources(string sid, string normalizedPath, bool updateFileSystem)
    {
        if (AclHelper.IsContainerSid(sid))
        {
            containerIuSync.RevertInteractiveUserGrant(sid, normalizedPath);
            return;
        }

        if (!AclHelper.IsLowIntegritySid(sid))
            lowIntegrityGrantSync.RevertSource(sid, normalizedPath, updateFileSystem);
    }

    public void ApplyAllowGrantSideEffects(string sid, string normalizedPath, SavedRightsState rights)
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
                    var entry = GrantEntryLookup.FindGrantEntryInDb(db, sid, normalizedPath, isDeny: false);
                    if (entry != null)
                        entry.SourceSids = sources;
                });
            }

            return;
        }

        dbAccessor.Write(db =>
        {
            var lowIlEntry = GrantEntryLookup.FindGrantEntryInDb(
                db, AclHelper.LowIntegritySid, normalizedPath, isDeny: false);
            if (lowIlEntry?.SourceSids != null &&
                !lowIlEntry.SourceSids.Contains(sid, StringComparer.OrdinalIgnoreCase))
                lowIlEntry.SourceSids.Add(sid);
        });
    }

    public bool HasRuntimeGrantEntry(string sid, string path, bool isDeny)
    {
        var normalizedPath = Path.GetFullPath(path);
        return dbAccessor.Read(db =>
            GrantEntryLookup.FindGrantEntryInDb(db, sid, normalizedPath, isDeny) != null);
    }

}
