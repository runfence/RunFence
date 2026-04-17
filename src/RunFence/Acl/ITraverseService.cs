using RunFence.Core.Models;

namespace RunFence.Acl;

/// <summary>
/// Operations for managing traverse ACEs: add, remove, cleanup orphaned, and fix.
/// Focused interface for consumers that only need traverse management.
/// </summary>
public interface ITraverseService
{
    /// <summary>
    /// Grants Traverse | ReadAttributes | Synchronize (no inheritance) on <paramref name="path"/>
    /// and every ancestor up to the drive root. Records a <see cref="GrantedPathEntry"/> with
    /// <c>IsTraverseOnly=true</c> in the database. For container SIDs, also grants traverse for
    /// the interactive user.
    /// Returns whether any ACE or DB entry was modified, plus the list of visited paths.
    /// </summary>
    (bool Modified, List<string> VisitedPaths) AddTraverse(string sid, string path);

    /// <summary>
    /// Removes traverse ACEs for <paramref name="sid"/> on <paramref name="path"/>, preserving
    /// paths still needed by other grants or traverse entries. Removes the
    /// <see cref="GrantedPathEntry"/> from the database.
    /// Returns true if a database entry was found and removed.
    /// </summary>
    bool RemoveTraverse(string sid, string path, bool updateFileSystem);

    /// <summary>
    /// Removes the traverse entry for <paramref name="path"/>'s directory (or <paramref name="path"/>
    /// itself when it is a directory) when no other allow grant on the same directory still needs it.
    /// </summary>
    void CleanupOrphanedTraverse(string sid, string path);

    /// <summary>
    /// Re-applies traverse ACEs for an existing traverse entry and returns the visited paths.
    /// </summary>
    List<string> FixTraverse(string sid, string path);
}
