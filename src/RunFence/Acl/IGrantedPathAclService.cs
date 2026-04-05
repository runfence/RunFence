using RunFence.Core.Models;

namespace RunFence.Acl;

/// <summary>
/// Reads and writes explicit NTFS ACEs for granted paths per SID.
/// Checkbox values (rights) are never stored — always read live from NTFS.
/// </summary>
public interface IGrantedPathAclService
{
    /// <summary>
    /// Reads the current ACE state for a SID on a path.
    /// Direct ACE for SID → Checked. Group ACE for any of groupSids → Indeterminate. None → Unchecked.
    /// </summary>
    GrantRightsState ReadRights(string path, string sid, IReadOnlyList<string> groupSids);

    /// <summary>
    /// Returns Available/Unavailable/Broken status for a path+sid combination.
    /// Broken = path exists but no direct ACE for this SID in the expected mode.
    /// </summary>
    PathAclStatus CheckPathStatus(string path, string sid, bool isDeny);

    /// <summary>
    /// Sets explicit Allow ACEs for this SID on the path. Read is always included.
    /// Removes the Allow ACE if all rights are unchecked.
    /// </summary>
    void ApplyAllowRights(string path, string sid, AllowRights rights);

    /// <summary>
    /// Sets explicit Deny ACEs for this SID on the path. Write+Special are always included.
    /// Removes the Deny ACE if all rights unchecked.
    /// </summary>
    void ApplyDenyRights(string path, string sid, DenyRights rights);

    /// <summary>
    /// Applies a Read-only Allow ACE for this SID (used when adding a new path).
    /// </summary>
    void ApplyReadOnlyGrant(string path, string sid);

    /// <summary>
    /// Removes only Allow or Deny explicit ACEs (based on isDeny) for this SID from path.
    /// </summary>
    void RevertGrant(string path, string sid, bool isDeny);

    /// <summary>
    /// Removes all explicit ACEs (both Allow and Deny) for this SID from the path.
    /// </summary>
    void RevertAllGrants(string path, string sid);

    /// <summary>
    /// Batch reverts all non-traverse grants for a SID. Skips IsTraverseOnly entries
    /// (traverse revert is handled by IUserTraverseService).
    /// Best-effort — logs but does not throw on individual failures.
    /// </summary>
    void RevertAllGrantsBatch(IEnumerable<GrantedPathEntry> grants, string sid);

    /// <summary>
    /// Changes the owner of path (and subdirectories/files when recursive) to the given SID.
    /// </summary>
    void ChangeOwner(string path, string sid, bool recursive);

    /// <summary>
    /// Changes the owner of path (and subdirectories/files when recursive) to BUILTIN\Administrators.
    /// </summary>
    void ResetOwner(string path, bool recursive);
}

/// <summary>Current ACE/ownership state for a path+SID combination.</summary>
public record GrantRightsState(
    CheckState AllowExecute,
    CheckState AllowWrite,
    CheckState AllowSpecial,
    CheckState DenyRead,
    CheckState DenyExecute,
    CheckState DenyWrite,
    CheckState DenySpecial,
    CheckState IsAccountOwner, // Allow mode Own: Checked=account owns, Indeterminate=group-owned
    bool IsAdminOwner, // Deny mode Own: true=BUILTIN\Administrators is owner
    int DirectAllowAceCount, // 0 + Allow mode = broken
    int DirectDenyAceCount); // 0 + Deny mode = broken

/// <summary>Rights for an Allow-mode grant. Read is always on.</summary>
public record AllowRights(bool Execute, bool Write, bool Special);

/// <summary>Rights for a Deny-mode grant. Write+Special are always on.</summary>
public record DenyRights(bool Read, bool Execute);

public enum PathAclStatus
{
    Available,
    Unavailable,
    Broken
}