using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;

namespace RunFence.Acl;

/// <summary>
/// Result returned by <see cref="GrantCoreOperations.AddGrant"/> for use by the orchestrator.
/// Does not include <c>TraverseAdded</c> — traverse coordination is the orchestrator's concern.
/// </summary>
public readonly record struct GrantAddResult(bool AlreadyExisted, bool DatabaseModified);

/// <summary>
/// Result returned by <see cref="GrantCoreOperations.RemoveGrant"/> for use by the orchestrator.
/// Communicates what was removed so the orchestrator can decide on traverse cleanup and IU revert.
/// </summary>
public readonly record struct GrantRemoveResult(bool Found, SavedRightsState? SavedRights);

/// <summary>
/// Pure NTFS+DB grant operations with no orchestration: no confirm callbacks, no traverse, no IU sync.
/// All DB access is marshaled to the UI thread via <see cref="UiThreadDatabaseAccessor"/>.
/// NTFS operations remain on the calling thread.
/// </summary>
public class GrantCoreOperations(
    IGrantAceService grantAceService,
    IFileOwnerService fileOwnerService,
    UiThreadDatabaseAccessor dbAccessor,
    ILoggingService log,
    IFileSystemPathInfo pathInfo) : IGrantCoreOperations
{
    /// <summary>
    /// Applies an allow or deny ACE for <paramref name="sid"/> on <paramref name="path"/> and
    /// records a <see cref="GrantedPathEntry"/> in the database.
    /// <para>
    /// Same-mode duplicate: updates <see cref="GrantedPathEntry.SavedRights"/> in-place (no new
    /// entry added), returns <c>AlreadyExisted=true</c>. Opposite-mode entry already exists:
    /// throws <see cref="InvalidOperationException"/>.
    /// </para>
    /// </summary>
    public GrantAddResult AddGrant(string sid, string path, bool isDeny,
        SavedRightsState? savedRights = null, string? ownerSid = null, bool? isFolderOverride = null)
    {
        var normalized = Path.GetFullPath(path);
        bool isFolder = isFolderOverride ?? pathInfo.DirectoryExists(normalized);
        var canAssignOwner = AclHelper.CanAssignGrantOwner(sid);
        var rights = AclHelper.ClearBlockedGrantOwner(sid, savedRights ?? SavedRightsState.DefaultForMode(isDeny))!;
        if (!canAssignOwner)
            ownerSid = null;

        var (hasSameMode, hasOppositeIsDeny) = dbAccessor.Read(db =>
        {
            var acct = db.GetAccount(sid);
            if (acct == null) return (false, (bool?)null);
            var conflict = GrantEntryLookup.FindNonTraverseGrantConflict(acct.Grants, normalized, isDeny);
            return (conflict.HasSameModeEntry, conflict.OppositeModeEntry?.IsDeny);
        });

        if (hasSameMode)
        {
            dbAccessor.Write(db =>
            {
                var entry = GrantEntryLookup.FindGrantEntryInDb(db, sid, normalized, isDeny);
                entry?.SavedRights = rights;
            });
            grantAceService.ApplyAce(normalized, sid, isDeny, rights, isFolder);
            if (ownerSid != null)
                fileOwnerService.ChangeOwner(normalized, ownerSid, recursive: false);
            return new GrantAddResult(AlreadyExisted: true, DatabaseModified: true);
        }

        if (hasOppositeIsDeny != null)
            throw new InvalidOperationException(
                $"An opposite-mode grant for '{normalized}' already exists for SID '{sid}'. " +
                $"Remove the existing {(hasOppositeIsDeny.Value ? "deny" : "allow")} grant first.");

        grantAceService.ApplyAce(normalized, sid, isDeny, rights, isFolder);

        if (ownerSid != null)
            fileOwnerService.ChangeOwner(normalized, ownerSid, recursive: false);

        dbAccessor.Write(db => db.GetOrCreateAccount(sid).Grants.Add(
            new GrantedPathEntry { Path = normalized, IsDeny = isDeny, SavedRights = rights }));

        return new GrantAddResult(AlreadyExisted: false, DatabaseModified: true);
    }

    /// <summary>
    /// Removes the allow or deny ACE for <paramref name="sid"/> on <paramref name="path"/>
    /// (when <paramref name="updateFileSystem"/> is true) and removes the
    /// <see cref="GrantedPathEntry"/> from the database.
    /// Returns the removed entry's <see cref="SavedRightsState"/> for orchestrator use
    /// (e.g. IU revert), or <c>Found=false</c> if no entry was present.
    /// </summary>
    public GrantRemoveResult RemoveGrant(string sid, string path, bool isDeny, bool updateFileSystem)
    {
        var normalized = Path.GetFullPath(path);

        var (found, savedRights) = dbAccessor.Read(db =>
        {
            var entry = GrantEntryLookup.FindGrantEntryInDb(db, sid, normalized, isDeny);
            return entry != null ? (true, entry.SavedRights) : (false, (SavedRightsState?)null);
        });

        if (!found)
            return new GrantRemoveResult(Found: false, SavedRights: null);

        if (updateFileSystem)
            grantAceService.RevertAce(normalized, sid, isDeny);

        dbAccessor.Write(db =>
        {
            var acct = db.GetAccount(sid);
            var e = acct != null ? GrantEntryLookup.FindGrantEntryInList(acct.Grants, normalized, isDeny) : null;
            if (e != null)
                acct!.Grants.Remove(e);
        });

        return new GrantRemoveResult(Found: true, SavedRights: savedRights);
    }

    /// <summary>
    /// Updates an existing grant's <see cref="GrantedPathEntry.SavedRights"/> and re-applies
    /// the NTFS ACE. Updates the owner if <paramref name="ownerSid"/> is provided.
    /// </summary>
    public void UpdateGrant(string sid, string path, bool isDeny,
        SavedRightsState savedRights, string? ownerSid = null, bool? isFolderOverride = null)
    {
        var normalized = Path.GetFullPath(path);
        bool isFolder = isFolderOverride ?? pathInfo.DirectoryExists(normalized);
        var canAssignOwner = AclHelper.CanAssignGrantOwner(sid);
        savedRights = AclHelper.ClearBlockedGrantOwner(sid, savedRights)!;
        if (!canAssignOwner)
            ownerSid = null;

        grantAceService.ApplyAce(normalized, sid, isDeny, savedRights, isFolder);

        if (ownerSid != null)
            fileOwnerService.ChangeOwner(normalized, ownerSid, recursive: false);

        dbAccessor.Write(db =>
        {
            var entry = GrantEntryLookup.FindGrantEntryInDb(db, sid, normalized, isDeny);
            entry?.SavedRights = savedRights;
        });
    }

    /// <summary>
    /// Re-applies the NTFS ACE for an existing grant entry (does not change the DB).
    /// </summary>
    public void FixGrant(string sid, string path, bool isDeny, bool? isFolderOverride = null)
    {
        var normalized = Path.GetFullPath(path);
        bool isFolder = isFolderOverride ?? pathInfo.DirectoryExists(normalized);

        var rights = dbAccessor.Read(db =>
        {
            var entry = GrantEntryLookup.FindGrantEntryInDb(db, sid, normalized, isDeny);
            return entry != null
                ? entry.SavedRights ?? SavedRightsState.DefaultForMode(isDeny)
                : null;
        });

        if (rights == null)
            return;

        grantAceService.ApplyAce(normalized, sid, isDeny, rights, isFolder);
    }

    /// <summary>
    /// Removes all grant (non-traverse) entries for <paramref name="sid"/>, reverting NTFS ACEs
    /// when <paramref name="updateFileSystem"/> is true. Returns the removed entries list for
    /// orchestrator use (IU revert). The DB clear is done by the caller after IU revert.
    /// </summary>
    public List<GrantedPathEntry> RemoveAllGrants(string sid, bool updateFileSystem)
    {
        var grants = dbAccessor.Read(db =>
        {
            var account = db.GetAccount(sid);
            return account?.Grants.Where(e => !e.IsTraverseOnly)
                .Select(e => e.Clone())
                .ToList() ?? [];
        });

        if (updateFileSystem)
        {
            foreach (var entry in grants)
            {
                try
                {
                    grantAceService.RevertAce(entry.Path, sid, entry.IsDeny);
                }
                catch (Exception ex)
                {
                    log.Warn($"RemoveAllGrants: failed to revert ACE on '{entry.Path}' for '{sid}': {ex.Message}");
                }
            }
        }

        return grants;
    }

    /// <summary>
    /// Validates a prospective grant against the DB: throws <see cref="InvalidOperationException"/>
    /// if a same-mode duplicate or an opposite-mode entry exists. No side effects.
    /// </summary>
    public void ValidateGrant(string sid, string path, bool isDeny)
    {
        var normalized = Path.GetFullPath(path);

        dbAccessor.Read(database =>
        {
            var account = database.GetAccount(sid);
            if (account == null) return;

            var conflict = GrantEntryLookup.FindNonTraverseGrantConflict(account.Grants, normalized, isDeny);
            if (conflict.HasSameModeEntry)
            {
                throw new InvalidOperationException(
                    $"A {(isDeny ? "deny" : "allow")} grant for '{normalized}' already exists for SID '{sid}'.");
            }

            if (conflict.OppositeModeEntry != null)
            {
                throw new InvalidOperationException(
                    $"An opposite-mode grant for '{normalized}' already exists for SID '{sid}'. " +
                    $"Remove the existing {(conflict.OppositeModeEntry.IsDeny ? "deny" : "allow")} grant first.");
            }
        });
    }

}
