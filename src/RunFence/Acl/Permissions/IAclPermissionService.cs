using System.Security.AccessControl;

namespace RunFence.Acl.Permissions;

/// <summary>
/// Injectable service for ACL permission checks and write operations: restricting access
/// and querying effective permissions.
/// </summary>
public interface IAclPermissionService
{
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
    /// When <paramref name="unelevated"/> is true, the Administrators group is excluded from the
    /// effective-rights check: an unelevated token cannot use Admins group for allow ACEs.
    /// </summary>
    bool NeedsPermissionGrant(string filePath, string accountSid,
        FileSystemRights requiredRights = FileSystemRights.ReadAndExecute, bool unelevated = false);

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