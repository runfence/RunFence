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
    IGrantNtfsHelper ntfs,
    UiThreadDatabaseAccessor dbAccessor,
    ILoggingService log)
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
        SavedRightsState? savedRights = null, string? ownerSid = null)
    {
        var normalized = Path.GetFullPath(path);
        bool isFolder = Directory.Exists(normalized);
        var rights = savedRights ?? SavedRightsState.DefaultForMode(isDeny);

        var (hasSameMode, hasOppositeIsDeny) = dbAccessor.Read(db =>
        {
            var acct = db.GetAccount(sid);
            if (acct == null) return (false, (bool?)null);
            bool same = FindGrantEntryInList(acct.Grants, normalized, isDeny) != null;
            var opp = FindGrantEntryInList(acct.Grants, normalized, !isDeny);
            return (same, opp != null ? (bool?)opp.IsDeny : null);
        });

        if (hasSameMode)
        {
            dbAccessor.Write(db =>
            {
                var entry = FindGrantEntryInDb(db, sid, normalized, isDeny);
                if (entry != null)
                    entry.SavedRights = rights;
            });
            ntfs.ApplyAce(normalized, sid, isDeny, rights, isFolder);
            if (ownerSid != null)
                ntfs.ChangeOwner(normalized, ownerSid, recursive: false);
            return new GrantAddResult(AlreadyExisted: true, DatabaseModified: true);
        }

        if (hasOppositeIsDeny != null)
            throw new InvalidOperationException(
                $"An opposite-mode grant for '{normalized}' already exists for SID '{sid}'. " +
                $"Remove the existing {(hasOppositeIsDeny.Value ? "deny" : "allow")} grant first.");

        ntfs.ApplyAce(normalized, sid, isDeny, rights, isFolder);

        if (ownerSid != null)
            ntfs.ChangeOwner(normalized, ownerSid, recursive: false);

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
            var entry = FindGrantEntryInDb(db, sid, normalized, isDeny);
            return entry != null ? (true, entry.SavedRights) : (false, (SavedRightsState?)null);
        });

        if (!found)
            return new GrantRemoveResult(Found: false, SavedRights: null);

        if (updateFileSystem)
            ntfs.RevertAce(normalized, sid, isDeny);

        dbAccessor.Write(db =>
        {
            var acct = db.GetAccount(sid);
            var e = acct != null ? FindGrantEntryInList(acct.Grants, normalized, isDeny) : null;
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
        SavedRightsState savedRights, string? ownerSid = null)
    {
        var normalized = Path.GetFullPath(path);
        bool isFolder = Directory.Exists(normalized);

        ntfs.ApplyAce(normalized, sid, isDeny, savedRights, isFolder);

        if (ownerSid != null)
            ntfs.ChangeOwner(normalized, ownerSid, recursive: false);

        dbAccessor.Write(db =>
        {
            var entry = FindGrantEntryInDb(db, sid, normalized, isDeny);
            if (entry != null)
                entry.SavedRights = savedRights;
        });
    }

    /// <summary>
    /// Re-applies the NTFS ACE for an existing grant entry (does not change the DB).
    /// </summary>
    public void FixGrant(string sid, string path, bool isDeny)
    {
        var normalized = Path.GetFullPath(path);
        bool isFolder = Directory.Exists(normalized);

        var rights = dbAccessor.Read(db =>
        {
            var entry = FindGrantEntryInDb(db, sid, normalized, isDeny);
            return entry != null
                ? entry.SavedRights ?? SavedRightsState.DefaultForMode(isDeny)
                : (SavedRightsState?)null;
        });

        if (rights == null)
            return;

        ntfs.ApplyAce(normalized, sid, isDeny, rights, isFolder);
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
                    ntfs.RevertAce(entry.Path, sid, entry.IsDeny);
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
    /// Returns the non-traverse grant entry for <paramref name="sid"/> on <paramref name="normalized"/>
    /// in the given <paramref name="database"/>, or null if not found.
    /// </summary>
    public static GrantedPathEntry? FindGrantEntryInDb(AppDatabase database, string sid,
        string normalized, bool isDeny)
    {
        var account = database.GetAccount(sid);
        return account != null ? FindGrantEntryInList(account.Grants, normalized, isDeny) : null;
    }

    /// <summary>
    /// Returns the non-traverse grant entry for <paramref name="normalized"/> in <paramref name="grants"/>,
    /// or null if not found.
    /// </summary>
    public static GrantedPathEntry? FindGrantEntryInList(List<GrantedPathEntry> grants,
        string normalized, bool isDeny)
        => grants.FirstOrDefault(e =>
            string.Equals(e.Path, normalized, StringComparison.OrdinalIgnoreCase) &&
            e.IsDeny == isDeny && !e.IsTraverseOnly);
}
