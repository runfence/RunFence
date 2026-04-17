namespace RunFence.Acl;

/// <summary>
/// Operations for syncing grants from the file system and managing ownership.
/// Focused interface for consumers that only need sync/ownership operations.
/// </summary>
public interface IGrantSyncService
{
    /// <summary>
    /// Reads actual NTFS ACEs for <paramref name="path"/> for a specific <paramref name="sid"/>
    /// (or all local-user SIDs if null). For each discovered ACE, creates or updates a matching
    /// <see cref="Core.Models.GrantedPathEntry"/> in the DB.
    /// Returns true if any DB entry was added or updated.
    /// </summary>
    bool UpdateFromPath(string path, string? sid = null);

    /// <summary>
    /// Changes the owner of <paramref name="path"/> (and contents when
    /// <paramref name="recursive"/>) to <paramref name="sid"/>.
    /// </summary>
    void ChangeOwner(string path, string sid, bool recursive);

    /// <summary>
    /// Changes the owner of <paramref name="path"/> (and contents when
    /// <paramref name="recursive"/>) to BUILTIN\Administrators.
    /// </summary>
    void ResetOwner(string path, bool recursive);
}
