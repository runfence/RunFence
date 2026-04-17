using System.Security.AccessControl;
using RunFence.Core.Models;

namespace RunFence.Acl;

/// <summary>
/// Path-type-aware mapping between <see cref="SavedRightsState"/> and <see cref="FileSystemRights"/>.
/// Single source of truth for all ACL rights conversions.
/// </summary>
public static class GrantRightsMapper
{
    public static readonly FileSystemRights ReadMask =
        FileSystemRights.ReadData | FileSystemRights.ReadAttributes |
        FileSystemRights.ReadExtendedAttributes | FileSystemRights.ReadPermissions |
        FileSystemRights.Synchronize;

    public static readonly FileSystemRights ExecuteMask = FileSystemRights.ExecuteFile;

    public static readonly FileSystemRights WriteFolderMask =
        FileSystemRights.WriteData | FileSystemRights.AppendData |
        FileSystemRights.WriteAttributes | FileSystemRights.WriteExtendedAttributes |
        FileSystemRights.DeleteSubdirectoriesAndFiles;

    public static readonly FileSystemRights WriteFileMask =
        FileSystemRights.WriteData | FileSystemRights.AppendData |
        FileSystemRights.WriteAttributes | FileSystemRights.WriteExtendedAttributes |
        FileSystemRights.Delete;

    public static readonly FileSystemRights SpecialFolderMask =
        FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership |
        FileSystemRights.Delete;

    public static readonly FileSystemRights SpecialFileMask =
        FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;

    public static readonly FileSystemRights TraverseOnlyMask =
        FileSystemRights.ExecuteFile | FileSystemRights.ReadAttributes |
        FileSystemRights.Synchronize;

    /// <summary>
    /// Maps a <see cref="SavedRightsState"/> to Allow <see cref="FileSystemRights"/>.
    /// Read is always included. Write and Special masks differ by path type.
    /// </summary>
    public static FileSystemRights MapAllowRights(SavedRightsState rights, bool isFolder)
    {
        var result = ReadMask;
        if (rights.Execute)
            result |= ExecuteMask;
        if (rights.Write)
            result |= isFolder ? WriteFolderMask : WriteFileMask;
        if (rights.Special)
            result |= isFolder ? SpecialFolderMask : SpecialFileMask;
        return result;
    }

    /// <summary>
    /// Maps a <see cref="SavedRightsState"/> to Deny <see cref="FileSystemRights"/>.
    /// Write+Special are always included. Write and Special masks differ by path type.
    /// </summary>
    public static FileSystemRights MapDenyRights(SavedRightsState rights, bool isFolder)
    {
        var result = (isFolder ? WriteFolderMask : WriteFileMask) |
                     (isFolder ? SpecialFolderMask : SpecialFileMask);
        if (rights.Read)
            result |= ReadMask;
        if (rights.Execute)
            result |= ExecuteMask;
        return result;
    }

    /// <summary>
    /// Reverse-maps NTFS allow or deny rights to a <see cref="SavedRightsState"/>.
    /// Used by UpdateFromPath and SavedRightsComparer to populate legacy entries from NTFS.
    /// </summary>
    public static SavedRightsState FromNtfsRights(
        FileSystemRights allowRights, FileSystemRights denyRights,
        bool isDeny, bool isFolder, RightCheckState isAccountOwner, bool isAdminOwner)
    {
        var writeMask = isFolder ? WriteFolderMask : WriteFileMask;
        var specialMask = isFolder ? SpecialFolderMask : SpecialFileMask;

        if (isDeny)
        {
            return new SavedRightsState(
                Execute: (denyRights & ExecuteMask) == ExecuteMask,
                Write: true,
                Read: (denyRights & ReadMask) == ReadMask,
                Special: true,
                Own: isAdminOwner);
        }

        return new SavedRightsState(
            Execute: (allowRights & ExecuteMask) == ExecuteMask,
            Write: (allowRights & writeMask) == writeMask,
            Read: true,
            Special: (allowRights & specialMask) == specialMask,
            Own: isAccountOwner == RightCheckState.Checked);
    }

    /// <summary>
    /// Maps <see cref="FileSystemRights"/> to a <see cref="SavedRightsState"/> by checking which
    /// rights masks are fully covered. Write and Special masks differ by path type.
    /// For allow mode, Read is always true and Own is always false.
    /// For deny mode, Write and Special are always true; Read and Execute reflect the rights bitmask.
    /// </summary>
    public static SavedRightsState FromRights(FileSystemRights rights, bool isFolder, bool isDeny)
    {
        if (isDeny)
        {
            return new SavedRightsState(
                Execute: (rights & ExecuteMask) == ExecuteMask,
                Write: true,
                Read: (rights & ReadMask) == ReadMask,
                Special: true,
                Own: false);
        }

        var writeMask = isFolder ? WriteFolderMask : WriteFileMask;
        var specialMask = isFolder ? SpecialFolderMask : SpecialFileMask;
        return new SavedRightsState(
            Execute: (rights & ExecuteMask) == ExecuteMask,
            Write: (rights & writeMask) == writeMask,
            Read: true,
            Special: (rights & specialMask) == specialMask,
            Own: false);
    }

    /// <summary>
    /// Returns true if <paramref name="rights"/> match only the traverse-only mask
    /// (ExecuteFile | ReadAttributes | Synchronize) — i.e. no broader grant rights are present.
    /// </summary>
    public static bool IsTraverseOnly(FileSystemRights rights)
        => rights != 0 && (rights & ~TraverseOnlyMask) == 0;
}
