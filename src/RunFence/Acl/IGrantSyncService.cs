namespace RunFence.Acl;

/// <summary>
/// Operations for syncing grants from the file system into tracked grant intent state
/// without mutating NTFS.
/// Focused interface for consumers that only need to read NTFS ACEs and update DB entries.
/// </summary>
public interface IGrantSyncService
{
    /// <summary>
     /// Reads actual NTFS ACEs for <paramref name="path"/> for a specific <paramref name="sid"/>
     /// (or all local-user SIDs if null). For each discovered ACE, creates or updates a matching
    /// <see cref="Core.Models.GrantedPathEntry"/> in tracked runtime/config intent state.
    /// Returns true if any DB entry was added or updated.
    /// </summary>
    bool UpdateFromPath(string path, string? sid = null);
}
