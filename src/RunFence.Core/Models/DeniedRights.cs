namespace RunFence.Core.Models;

/// <summary>
/// Cumulative deny rights levels for deny-mode ACLs. Each level includes
/// all rights from the previous level. Maps to specific FileSystemRights
/// via AclService.MapDeniedRights(). Intentionally excludes ReadPermissions,
/// ChangePermissions, TakeOwnership, and Synchronize from all levels.
/// </summary>
public enum DeniedRights
{
    /// <summary>Deny ExecuteFile only (current/default behavior).</summary>
    Execute = 0,

    /// <summary>Deny ExecuteFile + WriteData + AppendData + WriteAttributes + WriteExtendedAttributes + Delete + DeleteSubdirectoriesAndFiles.</summary>
    ExecuteWrite = 1,

    /// <summary>All of ExecuteWrite + ReadData + ReadAttributes + ReadExtendedAttributes.</summary>
    ExecuteReadWrite = 2
}