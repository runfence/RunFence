using System.Security;

namespace RunFence.Infrastructure;

public sealed class BackupIntentFileSystem(
    IBackupIntentNativeFileSystem nativeFileSystem,
    IBackupIntentManagedFileSystemProbe managedProbe) : IBackupIntentFileSystem
{
    public BackupIntentPathState GetFileState(string path)
    {
        return GetPathState(path, directory: false);
    }

    public BackupIntentPathState GetDirectoryState(string path)
    {
        return GetPathState(path, directory: true);
    }

    public bool TryEnumerateDirectories(string path, out IReadOnlyList<string> directories)
    {
        using var openResult = nativeFileSystem.TryOpen(path, directory: true);
        if (openResult.IsSuccess && nativeFileSystem.TryEnumerateDirectories(openResult.Handle!, path, out directories))
            return true;

        try
        {
            directories = managedProbe.EnumerateDirectories(path);
            return true;
        }
        catch
        {
            directories = [];
            return false;
        }
    }

    public bool TryGetDirectoryLastWriteTimeUtc(string path, out DateTime lastWriteTimeUtc)
    {
        lastWriteTimeUtc = default;

        using var openResult = nativeFileSystem.TryOpen(path, directory: true);
        if (!openResult.IsSuccess)
            return false;

        if (!nativeFileSystem.TryGetLastWriteTimeUtc(openResult.Handle!, out lastWriteTimeUtc))
        {
            try
            {
                lastWriteTimeUtc = managedProbe.GetDirectoryLastWriteTimeUtc(path);
                return lastWriteTimeUtc != default;
            }
            catch
            {
                return false;
            }
        }

        return true;
    }

    private BackupIntentPathState GetPathState(string path, bool directory)
    {
        using var openResult = nativeFileSystem.TryOpen(path, directory);
        if (openResult.IsSuccess)
            return BackupIntentPathState.Exists;

        if (IsDefinitivelyMissingStatus(openResult.Status, directory))
            return BackupIntentPathState.Missing;

        if (IsUnknownStatus(openResult.Status))
            return BackupIntentPathState.Unknown;

        return ProbePathState(path, directory);
    }

    private BackupIntentPathState ProbePathState(string path, bool directory)
    {
        try
        {
            var attributes = managedProbe.GetAttributes(path);
            var isDirectory = attributes.HasFlag(FileAttributes.Directory);
            if (directory)
                return isDirectory ? BackupIntentPathState.Exists : BackupIntentPathState.Missing;

            return isDirectory ? BackupIntentPathState.Missing : BackupIntentPathState.Exists;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return BackupIntentPathState.Missing;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException
                                   or IOException
                                   or NotSupportedException
                                   or SecurityException
                                   or ArgumentException
                                   or PathTooLongException)
        {
            return BackupIntentPathState.Unknown;
        }
    }

    private static bool IsDefinitivelyMissingStatus(int status, bool directory)
    {
        return status is BackupIntentNativeStatus.StatusObjectNameNotFound
            or BackupIntentNativeStatus.StatusObjectPathNotFound
            or BackupIntentNativeStatus.StatusNoSuchFile
            or BackupIntentNativeStatus.StatusNoSuchDevice
            || (directory && status == BackupIntentNativeStatus.StatusNotADirectory)
            || (!directory && status == BackupIntentNativeStatus.StatusFileIsADirectory);
    }

    private static bool IsUnknownStatus(int status)
    {
        return status is BackupIntentNativeStatus.StatusAccessDenied
            or BackupIntentNativeStatus.StatusSharingViolation
            or BackupIntentNativeStatus.StatusDeletePending
            or BackupIntentNativeStatus.StatusCannotDelete
            or BackupIntentNativeStatus.StatusPrivilegeNotHeld
            or BackupIntentNativeStatus.StatusObjectPathInvalid;
    }
}
