using System.Security.AccessControl;

namespace RunFence.Acl;

/// <summary>
/// Accumulated NTFS rights for a single path during an ACL scan.
/// OR-merged across multiple ACEs for the same SID+path.
/// </summary>
internal record struct PathAclAccumulator(
    bool HasAllow,
    bool HasDeny,
    FileSystemRights AllowRights,
    FileSystemRights DenyRights,
    bool IsAccountOwner,
    bool IsAdminOwner);

/// <summary>
/// Constants shared across ACL scan services.
/// </summary>
internal static class AclScanConstants
{
    /// <summary>
    /// Rights that constitute a "traverse-only" ACE — anything with additional rights is a full grant.
    /// </summary>
    public static readonly FileSystemRights TraverseOnlyMask =
        FileSystemRights.ExecuteFile | FileSystemRights.ReadAttributes | FileSystemRights.Synchronize;
}