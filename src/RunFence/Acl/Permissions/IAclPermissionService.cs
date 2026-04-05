using System.Security.AccessControl;
using RunFence.Core;

namespace RunFence.Acl.Permissions;

/// <summary>
/// Injectable service for ACL permission checks and write operations: granting rights,
/// restricting access, ensuring access with user confirmation, and querying effective permissions.
/// </summary>
public interface IAclPermissionService
{
    /// <summary>
    /// Grants <paramref name="rights"/> (default: ReadAndExecute with full inheritance
    /// on directories) to <paramref name="accountSid"/> on <paramref name="path"/>.
    /// </summary>
    void GrantRights(string path, string accountSid,
        FileSystemRights rights = FileSystemRights.ReadAndExecute);

    /// <summary>
    /// Checks and, if needed, grants <paramref name="rights"/> to <paramref name="accountSid"/>
    /// on the exact <paramref name="path"/> (caller resolves the directory if needed).
    /// <paramref name="confirm"/> returns true to proceed, false to skip; cancel throws
    /// <see cref="OperationCanceledException"/> from inside the callback. Null = silent grant.
    /// Returns true if an ACE was actually added, false otherwise (already sufficient / declined / error).
    /// </summary>
    bool EnsureRights(string path, string accountSid, FileSystemRights rights,
        ILoggingService logger, Func<string, bool>? confirm = null);

    /// <summary>
    /// Disables inheritance on a file and restricts access to SYSTEM (FullControl) and
    /// Administrators (FullControl) only. All other ACEs are removed.
    /// </summary>
    void RestrictToAdmins(string filePath);

    /// <summary>
    /// Computes whether <paramref name="accountSid"/> (and its group SIDs) has the
    /// <paramref name="requiredRights"/> effective access on the given security descriptor.
    /// </summary>
    bool HasEffectiveRights(FileSystemSecurity security, string accountSid,
        IReadOnlyList<string> accountGroupSids, FileSystemRights requiredRights);

    /// <summary>
    /// Returns the well-known group SIDs plus any local groups that <paramref name="accountSid"/>
    /// belongs to. Does NOT include the account SID itself.
    /// </summary>
    List<string> ResolveAccountGroupSids(string accountSid);

    /// <summary>
    /// Returns true if <paramref name="accountSid"/> does not have
    /// <paramref name="requiredRights"/> on <paramref name="filePath"/>.
    /// </summary>
    bool NeedsPermissionGrant(string filePath, string accountSid,
        FileSystemRights requiredRights = FileSystemRights.ReadAndExecute);

    /// <summary>
    /// Returns true if <paramref name="accountSid"/> needs ReadAndExecute on the directory
    /// containing <paramref name="filePath"/> (or <paramref name="filePath"/> itself if it is
    /// a directory).
    /// </summary>
    bool NeedsPermissionGrantOrParent(string filePath, string accountSid);

    /// <summary>
    /// Returns the grantable ancestor directories of <paramref name="filePath"/>, from
    /// most-specific to least-specific, stopping before blocked ACL roots and drive roots.
    /// </summary>
    IReadOnlyList<string> GetGrantableAncestors(string filePath);
}