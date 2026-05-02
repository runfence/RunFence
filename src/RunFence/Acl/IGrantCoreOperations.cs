using RunFence.Core.Models;

namespace RunFence.Acl;

/// <summary>
/// Pure NTFS+DB grant operations with no orchestration: no confirm callbacks, no traverse, no IU sync.
/// Focused interface for components that need direct grant mutation without higher-level coordination.
/// </summary>
public interface IGrantCoreOperations
{
    /// <summary>
    /// Applies an allow or deny ACE for <paramref name="sid"/> on <paramref name="path"/> and
    /// records a <see cref="GrantedPathEntry"/> in the database.
    /// Same-mode duplicate: updates <see cref="GrantedPathEntry.SavedRights"/> in-place,
    /// returns <c>AlreadyExisted=true</c>. Opposite-mode entry already exists: throws
    /// <see cref="InvalidOperationException"/>.
    /// </summary>
    GrantAddResult AddGrant(string sid, string path, bool isDeny,
        SavedRightsState? savedRights = null, string? ownerSid = null);

    /// <summary>
    /// Removes the allow or deny ACE for <paramref name="sid"/> on <paramref name="path"/>
    /// (when <paramref name="updateFileSystem"/> is true) and removes the
    /// <see cref="GrantedPathEntry"/> from the database.
    /// Returns the removed entry's <see cref="SavedRightsState"/> for orchestrator use,
    /// or <c>Found=false</c> if no entry was present.
    /// </summary>
    GrantRemoveResult RemoveGrant(string sid, string path, bool isDeny, bool updateFileSystem);

    /// <summary>
    /// Updates an existing grant's <see cref="GrantedPathEntry.SavedRights"/> and re-applies
    /// the NTFS ACE. Updates the owner if <paramref name="ownerSid"/> is provided.
    /// </summary>
    void UpdateGrant(string sid, string path, bool isDeny,
        SavedRightsState savedRights, string? ownerSid = null);

    /// <summary>
    /// Re-applies the NTFS ACE for an existing grant entry (does not change the DB).
    /// </summary>
    void FixGrant(string sid, string path, bool isDeny);

    /// <summary>
    /// Removes all grant (non-traverse) entries for <paramref name="sid"/>, reverting NTFS ACEs
    /// when <paramref name="updateFileSystem"/> is true. Returns the removed entries list for
    /// orchestrator use (IU revert). The DB clear is done by the caller after IU revert.
    /// </summary>
    List<GrantedPathEntry> RemoveAllGrants(string sid, bool updateFileSystem);

    /// <summary>
    /// Validates a prospective grant against the DB: throws <see cref="InvalidOperationException"/>
    /// if a same-mode duplicate or an opposite-mode entry exists. No side effects.
    /// </summary>
    void ValidateGrant(string sid, string path, bool isDeny);
}
