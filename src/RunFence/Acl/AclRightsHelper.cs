using System.Security.AccessControl;
using RunFence.Core.Models;

namespace RunFence.Acl;

/// <summary>
/// Single source of truth for mapping <see cref="DeniedRights"/> enum values to
/// <see cref="FileSystemRights"/> and for the full managed-deny rights mask.
/// </summary>
public static class AclRightsHelper
{
    public static FileSystemRights MapDeniedRights(DeniedRights deniedRights) => deniedRights switch
    {
        DeniedRights.Execute => FileSystemRights.ExecuteFile,
        DeniedRights.ExecuteWrite => FileSystemRights.ExecuteFile | FileSystemRights.WriteData |
                                     FileSystemRights.AppendData | FileSystemRights.WriteAttributes |
                                     FileSystemRights.WriteExtendedAttributes | FileSystemRights.Delete |
                                     FileSystemRights.DeleteSubdirectoriesAndFiles,
        DeniedRights.ExecuteReadWrite => FileSystemRights.ExecuteFile | FileSystemRights.WriteData |
                                         FileSystemRights.AppendData | FileSystemRights.WriteAttributes |
                                         FileSystemRights.WriteExtendedAttributes | FileSystemRights.Delete |
                                         FileSystemRights.DeleteSubdirectoriesAndFiles | FileSystemRights.ReadData |
                                         FileSystemRights.ReadAttributes | FileSystemRights.ReadExtendedAttributes,
        _ => FileSystemRights.ExecuteFile
    };

    /// <summary>
    /// Union of ALL rights that ANY <see cref="DeniedRights"/> value can produce.
    /// Used by <c>RemoveManagedDenyAces</c> to scope cleanup — never removes externally-placed deny ACEs.
    /// Must always be the full union (not per-app) to correctly clean up stale ACEs when downgrading.
    /// </summary>
    public static readonly FileSystemRights ManagedDenyRightsMask =
        MapDeniedRights(DeniedRights.Execute) |
        MapDeniedRights(DeniedRights.ExecuteWrite) |
        MapDeniedRights(DeniedRights.ExecuteReadWrite);
}