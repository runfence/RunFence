using System.Security.AccessControl;
using System.Text;
using Microsoft.Win32.SafeHandles;
using RunFence.Acl.Permissions;
using RunFence.Infrastructure;

namespace RunFence.Ipc;

/// <summary>
/// Validates that a path points to a real, locally-rooted directory the caller can read,
/// and holds the directory handle open to prevent TOCTOU swap attacks.
/// </summary>
public class DirectoryValidator(IAclPermissionService aclPermissionService) : IDirectoryValidator
{
    public DirectoryValidationHandle ValidateAndHold(string path, string callerSid)
    {
        // 1. Pre-validation: must be fully qualified local path (no UNC)
        if (!Path.IsPathFullyQualified(path))
            return Invalid("Path is not fully qualified.");
        if (path.StartsWith(@"\\", StringComparison.Ordinal))
            return Invalid("UNC paths are not supported.");

        // 2. Resolve reparse points (junctions/symlinks) to the real target
        var resolveResult = ResolveReparseChain(path);
        if (!resolveResult.Success)
            return Invalid(resolveResult.Error!);

        var realPath = resolveResult.ResolvedPath!;

        // 3. Open and lock: no FILE_SHARE_DELETE to prevent deletion/rename while held
        var hFile = NativeMethods.CreateFile(realPath, NativeMethods.GENERIC_READ,
            NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE,
            IntPtr.Zero, NativeMethods.OPEN_EXISTING,
            NativeMethods.FILE_FLAG_BACKUP_SEMANTICS, IntPtr.Zero);
        if (hFile == NativeMethods.INVALID_HANDLE_VALUE)
            return Invalid("Could not open directory.");

        // 4. Verify it is actually a directory
        if (!NativeMethods.GetFileInformationByHandle(hFile, out var info))
        {
            NativeMethods.CloseHandle(hFile);
            return Invalid("Could not read file attributes.");
        }

        if ((info.dwFileAttributes & NativeMethods.FILE_ATTRIBUTE_DIRECTORY) == 0)
        {
            NativeMethods.CloseHandle(hFile);
            return Invalid("Path is not a directory.");
        }

        // 5. Get canonical path via the held handle (final sanity check after reparse resolution)
        var sb = new StringBuilder(32768);
        var len = NativeMethods.GetFinalPathNameByHandle(hFile, sb, (uint)sb.Capacity, 0);
        if (len == 0 || len > sb.Capacity)
        {
            NativeMethods.CloseHandle(hFile);
            return Invalid("Could not resolve canonical path.");
        }

        var canonicalPath = sb.ToString();
        // Strip extended-length path prefix added by GetFinalPathNameByHandle
        if (canonicalPath.StartsWith(@"\\?\", StringComparison.Ordinal))
            canonicalPath = canonicalPath[4..];

        // 6. Verify the caller can list this directory
        if (aclPermissionService.NeedsPermissionGrant(canonicalPath, callerSid, FileSystemRights.ListDirectory))
        {
            NativeMethods.CloseHandle(hFile);
            return Invalid("Caller does not have access to the directory.");
        }

        // 7. Return with handle held open — caller disposes after launch
        var safeHandle = new SafeFileHandle(hFile, ownsHandle: true);
        return new DirectoryValidationHandle(safeHandle)
        {
            IsValid = true,
            CanonicalPath = canonicalPath
        };
    }

    private static DirectoryValidationHandle Invalid(string error) => new(null) { IsValid = false, Error = error };

    // ── Reparse-chain resolution ──────────────────────────────────────────────

    private readonly record struct ResolveResult(bool Success, string? ResolvedPath = null, string? Error = null);

    private static ResolveResult ResolveReparseChain(string inputPath)
    {
        var currentPath = inputPath;

        for (int depth = 0; depth < 5; depth++)
        {
            var attrs = NativeMethods.GetFileAttributes(currentPath);
            if (attrs == NativeMethods.INVALID_FILE_ATTRIBUTES)
                return new ResolveResult(false, Error: "Path does not exist or is inaccessible.");

            if ((attrs & NativeMethods.FILE_ATTRIBUTE_REPARSE_POINT) == 0)
                return new ResolveResult(true, ResolvedPath: currentPath);

            // Open the reparse point itself (not following it) to read the target
            var hRp = NativeMethods.CreateFile(currentPath, NativeMethods.GENERIC_READ,
                NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE,
                IntPtr.Zero, NativeMethods.OPEN_EXISTING,
                NativeMethods.FILE_FLAG_BACKUP_SEMANTICS | NativeMethods.FILE_FLAG_OPEN_REPARSE_POINT,
                IntPtr.Zero);
            if (hRp == NativeMethods.INVALID_HANDLE_VALUE)
                return new ResolveResult(false, Error: "Could not open reparse point.");

            try
            {
                var buffer = new byte[NativeMethods.MAXIMUM_REPARSE_DATA_BUFFER_SIZE];
                bool ok = NativeMethods.DeviceIoControl(hRp, NativeMethods.FSCTL_GET_REPARSE_POINT,
                    IntPtr.Zero, 0, buffer, (uint)buffer.Length, out _, IntPtr.Zero);
                if (!ok)
                    return new ResolveResult(false, Error: "Could not read reparse point data.");

                var target = ParseReparseSubstituteName(buffer);
                if (target == null)
                    return new ResolveResult(false, Error: "Unsupported or unknown reparse point type.");

                if (target.StartsWith(@"\\", StringComparison.Ordinal))
                    return new ResolveResult(false, Error: "Reparse point target is a UNC path, which is not allowed.");

                currentPath = target;
            }
            finally
            {
                NativeMethods.CloseHandle(hRp);
            }
        }

        return new ResolveResult(false, Error: "Reparse point chain too deep (max 5 levels).");
    }

    /// <summary>
    /// Parses the substitute name from a REPARSE_DATA_BUFFER byte array.
    /// Returns null for unsupported reparse tags.
    /// </summary>
    private static string? ParseReparseSubstituteName(byte[] buffer)
    {
        if (buffer.Length < 16)
            return null;

        var reparseTag = BitConverter.ToUInt32(buffer, 0);
        if (reparseTag != NativeMethods.IO_REPARSE_TAG_SYMLINK &&
            reparseTag != NativeMethods.IO_REPARSE_TAG_MOUNT_POINT)
            return null;

        // Both SymbolicLink and MountPoint share the same offsets 8..15 for name lengths.
        // PathBuffer starts at offset 20 for symlinks (extra 4-byte Flags field),
        // and at offset 16 for mount points.
        var substituteNameOffset = BitConverter.ToUInt16(buffer, 8);
        var substituteNameLength = BitConverter.ToUInt16(buffer, 10);
        if (substituteNameLength == 0)
            return null;

        int pathBufferStart = reparseTag == NativeMethods.IO_REPARSE_TAG_SYMLINK ? 20 : 16;
        int absoluteOffset = pathBufferStart + substituteNameOffset;
        if (absoluteOffset + substituteNameLength > buffer.Length)
            return null;

        var name = Encoding.Unicode.GetString(buffer, absoluteOffset, substituteNameLength);

        // Strip NT namespace prefix (\??\) to get a regular local path
        if (name.StartsWith(@"\??\", StringComparison.Ordinal))
            name = name[4..];

        return name;
    }
}