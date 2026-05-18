using RunFence.Core.Models;
using RunFence.Persistence;

namespace RunFence.Acl;

/// <summary>
/// Operations for managing traverse ACEs: add, remove, cleanup orphaned, and fix.
/// Focused interface for consumers that only need traverse management.
/// </summary>
public interface ITraverseService
{
    GrantApplyResult AddTraverse(string accountSid, string path, IGrantIntentStore? store = null);

    GrantApplyResult RemoveTraverse(string accountSid, string path);

    /// <summary>
    /// Restores the tracked traverse entry at <paramref name="path"/> to the exact prior
    /// <paramref name="previousEntry"/> state and reconciles the corresponding traverse ACEs.
    /// When <paramref name="previousEntry"/> is null, removes the current tracked traverse entry.
    /// </summary>
    GrantApplyResult RestoreTraverse(string accountSid, string path,
        GrantIntentRestoreSnapshot previousState);

    GrantApplyResult UntrackTraverse(string accountSid, string path);

    /// <summary>
    /// Removes the traverse entry for <paramref name="path"/>'s directory (or <paramref name="path"/>
    /// itself when it is a directory) when no other allow grant on the same directory still needs it.
    /// </summary>
    void CleanupOrphanedTraverse(string sid, string path);

    /// <summary>
    /// Re-applies traverse ACEs for an existing traverse entry and returns the visited paths.
    /// </summary>
    List<string> FixTraverse(string sid, string path);

    GrantApplyResult FixTraverseAcl(string accountSid, string path);
}
