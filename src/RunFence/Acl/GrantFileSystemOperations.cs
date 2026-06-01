using System.Security.AccessControl;
using RunFence.Core.Models;
using RunFence.Infrastructure;

namespace RunFence.Acl;

/// <summary>
/// Low-level grant filesystem operations: ACE mutation/status, owner mutation, mandatory label
/// handling, and DB write-through for grant records. This class does not perform confirm-callback
/// workflows; see <see cref="GrantAccessEnsurer"/>.
/// </summary>
public class GrantFileSystemOperations(
    IGrantCoreOperations grantCore,
    IGrantAceService grantAceService,
    IFileOwnerService fileOwnerService,
    IMandatoryLabelService mandatoryLabelService,
    UiThreadDatabaseAccessor dbAccessor)
{
    public GrantedPathEntry PrepareGrantEntryForPersistence(string sid, GrantedPathEntry entry,
        SavedRightsState? previousSavedRights = null, string? previousSaclLabel = null)
        => PrepareLowIntegrityGrantPersistence(sid, entry, previousSavedRights, previousSaclLabel).Entry;

    public GrantOperationResult AddGrant(string sid, string path, bool isDeny,
        SavedRightsState? savedRights = null, string? ownerSid = null, bool? isFolderOverride = null,
        string? desiredPreviousSaclLabel = null)
    {
        var normalized = Path.GetFullPath(path);
        var rights = savedRights ?? SavedRightsState.DefaultForMode(isDeny);
        var preparedEntry = PrepareGrantEntryForPersistence(
            sid,
            new GrantedPathEntry
            {
                Path = normalized,
                IsDeny = isDeny,
                SavedRights = rights,
                PreviousSaclLabel = desiredPreviousSaclLabel
            });

        var coreResult = grantCore.AddGrant(sid, normalized, isDeny, rights, ownerSid, isFolderOverride);

        if (!isDeny && AclHelper.IsLowIntegritySid(sid))
        {
            dbAccessor.Write(db =>
            {
                var entry = GrantEntryLookup.FindGrantEntryInDb(db, sid, normalized, isDeny: false);
                if (entry != null)
                    entry.PreviousSaclLabel = preparedEntry.PreviousSaclLabel;
            });

            if (rights.Write)
                mandatoryLabelService.ApplyLowIntegrityLabel(normalized);
        }

        return new GrantOperationResult(
            GrantAdded: !coreResult.AlreadyExisted,
            TraverseAdded: false,
            DatabaseModified: coreResult.DatabaseModified);
    }

    public bool RemoveGrant(string sid, string path, bool isDeny, bool updateFileSystem)
    {
        var normalized = Path.GetFullPath(path);

        bool hadWrite = false;
        string? previousSaclLabel = null;
        if (!isDeny && AclHelper.IsLowIntegritySid(sid))
        {
            dbAccessor.Read(db =>
            {
                var entry = GrantEntryLookup.FindGrantEntryInDb(db, sid, normalized, isDeny: false);
                hadWrite = entry?.SavedRights?.Write == true;
                previousSaclLabel = entry?.PreviousSaclLabel;
            });
        }

        var coreResult = grantCore.RemoveGrant(sid, normalized, isDeny, updateFileSystem);
        if (!coreResult.Found)
            return false;

        if (!isDeny && AclHelper.IsLowIntegritySid(sid) && updateFileSystem && hadWrite)
            mandatoryLabelService.RestoreMandatoryLabel(normalized, previousSaclLabel);

        return true;
    }

    public GrantOperationResult UpdateGrant(string sid, string path, bool isDeny,
        SavedRightsState savedRights, string? ownerSid = null, bool? isFolderOverride = null,
        SavedRightsState? previousSavedRights = null, string? previousSaclLabel = null,
        string? desiredPreviousSaclLabel = null)
    {
        var normalized = Path.GetFullPath(path);
        var (preparedEntry, oldHadWrite, oldPreviousSaclLabel) = PrepareLowIntegrityGrantPersistence(
            sid,
            new GrantedPathEntry
            {
                Path = normalized,
                IsDeny = isDeny,
                SavedRights = savedRights,
                PreviousSaclLabel = desiredPreviousSaclLabel
            },
            previousSavedRights,
            previousSaclLabel);

        grantCore.UpdateGrant(sid, normalized, isDeny, savedRights, ownerSid, isFolderOverride);

        if (!isDeny && AclHelper.IsLowIntegritySid(sid))
        {
            dbAccessor.Write(db =>
            {
                var entry = GrantEntryLookup.FindGrantEntryInDb(db, sid, normalized, isDeny: false);
                if (entry != null)
                    entry.PreviousSaclLabel = preparedEntry.PreviousSaclLabel;
            });

            bool newHasWrite = savedRights.Write;
            if (!oldHadWrite && newHasWrite)
            {
                mandatoryLabelService.ApplyLowIntegrityLabel(normalized);
            }
            else if (oldHadWrite && !newHasWrite)
            {
                mandatoryLabelService.RestoreMandatoryLabel(normalized, oldPreviousSaclLabel);
            }
        }

        return new GrantOperationResult(GrantAdded: false, TraverseAdded: false, DatabaseModified: true);
    }

    public void FixGrant(string sid, string path, bool isDeny, bool? isFolderOverride = null)
        => grantCore.FixGrant(sid, path, isDeny, isFolderOverride);

    public GrantRightsState ReadGrantState(string path, string sid, IReadOnlyList<string> groupSids)
        => grantAceService.ReadGrantState(path, sid, groupSids);

    public PathAclStatus CheckGrantStatus(string path, string sid, bool isDeny)
        => grantAceService.CheckGrantStatus(path, sid, isDeny);

    public void ValidateGrant(string sid, string path, bool isDeny)
        => grantCore.ValidateGrant(sid, path, isDeny);

    public void ChangeOwner(string path, string sid, bool recursive)
        => fileOwnerService.ChangeOwner(path, sid, recursive);

    public void ResetOwner(string path, bool recursive)
        => fileOwnerService.ResetOwner(path, recursive);

    private (GrantedPathEntry Entry, bool OldHadWrite, string? OldPreviousSaclLabel) PrepareLowIntegrityGrantPersistence(
        string sid,
        GrantedPathEntry entry,
        SavedRightsState? previousSavedRights = null,
        string? previousSaclLabel = null)
    {
        var preparedEntry = entry.Clone();
        if (preparedEntry.IsDeny || !AclHelper.IsLowIntegritySid(sid))
            return (preparedEntry, false, null);

        var normalized = Path.GetFullPath(preparedEntry.Path);
        preparedEntry.Path = normalized;
        var (oldHadWrite, oldPreviousSaclLabel) = ResolvePreviousLowIntegrityWriteState(
            sid,
            normalized,
            previousSavedRights,
            previousSaclLabel);
        bool newHasWrite = preparedEntry.SavedRights?.Write == true;

        if (!newHasWrite)
        {
            preparedEntry.PreviousSaclLabel = null;
            return (preparedEntry, oldHadWrite, oldPreviousSaclLabel);
        }

        if (oldHadWrite)
        {
            preparedEntry.PreviousSaclLabel = oldPreviousSaclLabel;
            return (preparedEntry, oldHadWrite, oldPreviousSaclLabel);
        }

        preparedEntry.PreviousSaclLabel ??= mandatoryLabelService.ReadMandatoryLabel(normalized);
        return (preparedEntry, oldHadWrite, oldPreviousSaclLabel);
    }

    private (bool HadWrite, string? PreviousSaclLabel) ResolvePreviousLowIntegrityWriteState(
        string sid,
        string normalizedPath,
        SavedRightsState? previousSavedRights,
        string? previousSaclLabel)
    {
        if (previousSavedRights != null || previousSaclLabel != null)
            return (previousSavedRights?.Write == true || previousSaclLabel != null, previousSaclLabel);

        bool hadWrite = false;
        string? oldPreviousSaclLabel = null;
        dbAccessor.Read(db =>
        {
            var entry = GrantEntryLookup.FindGrantEntryInDb(db, sid, normalizedPath, isDeny: false);
            hadWrite = entry?.SavedRights?.Write == true || entry?.PreviousSaclLabel != null;
            oldPreviousSaclLabel = entry?.PreviousSaclLabel;
        });

        return (hadWrite, oldPreviousSaclLabel);
    }
}
