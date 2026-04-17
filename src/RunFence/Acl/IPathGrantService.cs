using RunFence.Core.Models;

namespace RunFence.Acl;

/// <summary>
/// Result returned by grant mutation methods, capturing what was added and whether the
/// in-memory database was modified.
/// </summary>
/// <param name="GrantAdded">True if a new allow/deny ACE was applied on the target path itself.</param>
/// <param name="TraverseAdded">True if new traverse ACEs were applied on ancestor directories.</param>
/// <param name="DatabaseModified">
/// True if the in-memory database was written (main grant, traverse, or interactive user sync).
/// </param>
public readonly record struct GrantOperationResult(bool GrantAdded, bool TraverseAdded, bool DatabaseModified);

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
    IGrantSyncService;

/// <summary>Current ACE/ownership state for a path+SID combination.</summary>
public record GrantRightsState(
    RightCheckState AllowExecute,
    RightCheckState AllowWrite,
    RightCheckState AllowSpecial,
    RightCheckState DenyRead,
    RightCheckState DenyExecute,
    RightCheckState DenyWrite,
    RightCheckState DenySpecial,
    RightCheckState IsAccountOwner, // Allow mode Own: Checked=account owns, Indeterminate=group-owned
    bool IsAdminOwner, // Deny mode Own: true=BUILTIN\Administrators is owner
    int DirectAllowAceCount, // 0 + Allow mode = broken
    int DirectDenyAceCount); // 0 + Deny mode = broken

public enum PathAclStatus
{
    Available,
    Unavailable,
    Broken
}
