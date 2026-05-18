using RunFence.Core.Models;
using RunFence.Persistence;

namespace RunFence.Acl;

/// <summary>
/// Single source of truth for all grant and traverse ACE operations, DB tracking, and
/// container interactive-user sync. All writes to <see cref="AccountEntry.Grants"/> and all
/// grant/traverse NTFS ACE operations go through this service.
/// Composed of focused sub-interfaces; existing consumers that inject <see cref="IPathGrantService"/>
/// are unchanged. New consumers that need only a subset inject the focused interface.
/// </summary>
public interface IPathGrantService :
    IGrantMutatorService,
    ITraverseService,
    IGrantInspectionService,
    IGrantIntentSnapshotService,
    IGrantSyncService
{
    /// <summary>
    /// Validates a prospective grant against the DB: throws <see cref="InvalidOperationException"/>
    /// if a same-mode duplicate or an opposite-mode entry exists. No side effects.
    /// </summary>
    void ValidateGrant(string sid, string path, bool isDeny);

    /// <summary>
    /// Changes the owner of <paramref name="path"/> (and contents when
    /// <paramref name="recursive"/>) to BUILTIN\Administrators.
    /// </summary>
    void ResetOwner(string path, bool recursive);
}
