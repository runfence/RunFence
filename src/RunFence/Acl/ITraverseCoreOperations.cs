using RunFence.Core.Models;

namespace RunFence.Acl;

/// <summary>
/// Pure NTFS+DB traverse operations, fully independent of grant logic and IU sync.
/// Focused interface for components that need direct traverse mutation without higher-level coordination.
/// </summary>
public interface ITraverseCoreOperations
{
    IReadOnlyList<string> CollectCoveragePaths(string path);

    IReadOnlyList<string> GetPathsNeedingTraverseAce(string sid, IReadOnlyList<string> coveragePaths);

    bool TrackTraverse(string sid, GrantedPathEntry entry);

    IReadOnlyList<string> ApplyTraverseAces(string sid, IReadOnlyList<string> paths);

    void RemoveTraverseAces(string sid, IReadOnlyList<string> paths);

    void VerifyEffectiveTraverse(string sid, IReadOnlyList<string> paths);

    /// <summary>
    /// Grants Traverse | ReadAttributes | Synchronize (no inheritance) on <paramref name="path"/>
    /// and every ancestor up to the drive root. Records a <see cref="GrantedPathEntry"/> with
    /// <c>IsTraverseOnly=true</c> in the database.
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
    /// Re-applies traverse ACEs for an existing traverse entry and returns the visited paths.
    /// </summary>
    List<string> FixTraverse(string sid, string path);

    /// <summary>
    /// Removes the traverse entry for <paramref name="normalizedGrantPath"/>'s directory when no
    /// other allow grant on the same directory still needs it.
    /// </summary>
    void CleanupOrphanedTraverse(string sid, string normalizedGrantPath);

    /// <summary>
    /// Reverts NTFS traverse ACEs for all traverse entries in <paramref name="allGrantsSnapshot"/>.
    /// Used by the orchestrator's <c>RemoveAll</c> when bulk-removing all entries for a SID.
    /// </summary>
    void RevertAllTraverseAces(string sid, IReadOnlyList<GrantedPathEntry> allGrantsSnapshot);
}
