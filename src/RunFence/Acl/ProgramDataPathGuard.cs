using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using RunFence.Core;
using RunFence.Infrastructure;

namespace RunFence.Acl;

public class ProgramDataPathGuard : IProgramDataPathGuard, IProgramDataPathPolicyService
{
    private readonly string rootPath = NormalizePath(PathConstants.ProgramDataDir);

    public string NormalizeRoot() => rootPath;

    public string NormalizeRelativePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new InvalidOperationException("ProgramData relative path must not be empty.");
        }

        if (Path.IsPathFullyQualified(relativePath) || Path.IsPathRooted(relativePath))
        {
            throw new InvalidOperationException("ProgramData relative path must not be absolute.");
        }

        ValidateRelativeSegments(relativePath);
        var normalized = NormalizePath(Path.Combine(rootPath, relativePath));
        EnsureUnderRoot(normalized);
        return normalized;
    }

    public string NormalizeExistingPathUnderRoot(string path, ProgramDataObjectKind kind)
    {
        var normalized = NormalizeAbsolutePathUnderRoot(path);
        ValidateExistingManagedPath(normalized, kind);
        return normalized;
    }

    public SafeFileHandle OpenExistingManagedObject(string path, ProgramDataObjectKind kind, ProgramDataManagedObjectAccess access)
    {
        var normalized = NormalizeExistingPathUnderRoot(path, kind);
        var handle = OpenHandle(normalized, MapDesiredAccess(access));
        try
        {
            ValidateFinalHandle(handle, kind, normalized);
            return handle;
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    public bool IsUnderRoot(string path)
    {
        try
        {
            if (!Path.IsPathFullyQualified(path))
            {
                return false;
            }

            var normalized = NormalizePath(path);
            return IsPathWithinRoot(normalized);
        }
        catch
        {
            return false;
        }
    }

    public string NormalizeAbsolutePathUnderRoot(string path)
    {
        if (!Path.IsPathFullyQualified(path))
        {
            throw new InvalidOperationException("ProgramData managed path must be fully qualified.");
        }

        var normalized = NormalizePath(path);
        EnsureUnderRoot(normalized);
        return normalized;
    }

    private void EnsureUnderRoot(string path)
    {
        if (!IsPathWithinRoot(path))
        {
            throw new InvalidOperationException($"ProgramData managed path '{path}' is outside '{rootPath}'.");
        }
    }

    private bool IsPathWithinRoot(string path)
    {
        if (string.Equals(path, rootPath, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return path.StartsWith(rootPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private void ValidateExistingManagedPath(string path, ProgramDataObjectKind kind)
    {
        ValidateExistingDirectorySegment(rootPath);
        if (string.Equals(path, rootPath, StringComparison.OrdinalIgnoreCase))
        {
            if (kind != ProgramDataObjectKind.Directory)
            {
                throw new InvalidOperationException("ProgramData root is a directory.");
            }

            return;
        }

        var relativePath = Path.GetRelativePath(rootPath, path);
        var segments = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var current = rootPath;
        for (int i = 0; i < segments.Length; i++)
        {
            current = Path.Combine(current, segments[i]);
            bool isLast = i == segments.Length - 1;
            if (isLast && kind == ProgramDataObjectKind.File)
            {
                ValidateExistingFileSegment(current);
            }
            else
            {
                ValidateExistingDirectorySegment(current);
            }
        }
    }

    private static void ValidateRelativeSegments(string relativePath)
    {
        foreach (var rawSegment in relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (string.IsNullOrWhiteSpace(rawSegment))
            {
                throw new InvalidOperationException("ProgramData relative path must not contain empty segments.");
            }

            if (rawSegment is "." or "..")
            {
                throw new InvalidOperationException("ProgramData relative path must not contain '.' or '..' segments.");
            }
        }
    }

    private void ValidateExistingDirectorySegment(string directoryPath)
    {
        using var handle = OpenHandle(directoryPath, FileSecurityNative.FILE_LIST_DIRECTORY | FileSecurityNative.FILE_READ_ATTRIBUTES);
        ValidateDirectoryHandle(handle, directoryPath);
    }

    private void ValidateExistingFileSegment(string filePath)
    {
        using var handle = OpenHandle(filePath, FileSecurityNative.FILE_READ_DATA | FileSecurityNative.FILE_READ_ATTRIBUTES);
        ValidateFileHandle(handle, filePath);
    }

    private static void ValidateFinalHandle(SafeFileHandle handle, ProgramDataObjectKind kind, string path)
    {
        if (kind == ProgramDataObjectKind.Directory)
        {
            ValidateDirectoryHandle(handle, path);
            return;
        }

        ValidateFileHandle(handle, path);
    }

    private static void ValidateDirectoryHandle(SafeFileHandle handle, string path)
    {
        var info = GetHandleInfo(handle, path);
        if ((info.dwFileAttributes & FileSecurityNative.FILE_ATTRIBUTE_DIRECTORY) == 0)
        {
            throw new InvalidOperationException($"Managed ProgramData object '{path}' is not a directory.");
        }

        if ((info.dwFileAttributes & FileSecurityNative.FILE_ATTRIBUTE_REPARSE_POINT) != 0)
        {
            throw new InvalidOperationException($"Managed ProgramData directory '{path}' must not be a reparse point.");
        }
    }

    private static void ValidateFileHandle(SafeFileHandle handle, string path)
    {
        var info = GetHandleInfo(handle, path);
        if ((info.dwFileAttributes & FileSecurityNative.FILE_ATTRIBUTE_DIRECTORY) != 0)
        {
            throw new InvalidOperationException($"Managed ProgramData object '{path}' is not a file.");
        }

        if ((info.dwFileAttributes & FileSecurityNative.FILE_ATTRIBUTE_REPARSE_POINT) != 0)
        {
            throw new InvalidOperationException($"Managed ProgramData file '{path}' must not be a reparse point.");
        }
    }

    private static FileSecurityNative.BY_HANDLE_FILE_INFORMATION GetHandleInfo(SafeFileHandle handle, string path)
    {
        if (!FileSecurityNative.GetFileInformationByHandle(handle.DangerousGetHandle(), out var info))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Could not read attributes for '{path}'.");
        }

        return info;
    }

    private static SafeFileHandle OpenHandle(string path, uint desiredAccess)
    {
        var flags = FileSecurityNative.FILE_FLAG_BACKUP_SEMANTICS | FileSecurityNative.FILE_FLAG_OPEN_REPARSE_POINT;
        var handle = FileSecurityNative.CreateFile(
            path,
            desiredAccess,
            FileSecurityNative.FILE_SHARE_READ | FileSecurityNative.FILE_SHARE_WRITE,
            IntPtr.Zero,
            FileSecurityNative.OPEN_EXISTING,
            flags,
            IntPtr.Zero);
        if (handle == FileSecurityNative.INVALID_HANDLE_VALUE)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Could not open managed ProgramData object '{path}'.");
        }

        return new SafeFileHandle(handle, ownsHandle: true);
    }

    private static uint MapDesiredAccess(ProgramDataManagedObjectAccess access) =>
        access switch
        {
            ProgramDataManagedObjectAccess.Validate => FileSecurityNative.READ_CONTROL,
            ProgramDataManagedObjectAccess.OwnerRepair => FileSecurityNative.READ_CONTROL | FileSecurityNative.WRITE_OWNER,
            ProgramDataManagedObjectAccess.DaclRepair => FileSecurityNative.READ_CONTROL | FileSecurityNative.WRITE_DAC | FileSecurityNative.WRITE_OWNER,
            _ => throw new ArgumentOutOfRangeException(nameof(access))
        };

    private static string NormalizePath(string path)
        => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
