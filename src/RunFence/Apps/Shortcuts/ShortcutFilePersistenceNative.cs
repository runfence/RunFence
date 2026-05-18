using System.ComponentModel;
using System.Security.AccessControl;
using Microsoft.Win32.SafeHandles;
using RunFence.Acl;
using RunFence.Infrastructure;

namespace RunFence.Apps.Shortcuts;

public sealed class ShortcutFilePersistenceNative(
    BackupPrivilegeSecurityDescriptorAccessor backupPrivilegeSecurityDescriptorAccessor) : IShortcutFilePersistenceNative
{
    public ShortcutFileMetadata? TryCaptureExistingMetadata(string shortcutPath)
    {
        if (!File.Exists(shortcutPath))
            return null;

        var fileInfo = new FileInfo(shortcutPath);
        return new ShortcutFileMetadata(
            fileInfo.GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner),
            File.GetAttributes(shortcutPath),
            File.GetCreationTimeUtc(shortcutPath),
            File.GetLastWriteTimeUtc(shortcutPath),
            File.GetLastAccessTimeUtc(shortcutPath));
    }

    public void DeleteExistingDestination(string shortcutPath)
    {
        if (!File.Exists(shortcutPath))
            return;

        try
        {
            File.Delete(shortcutPath);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            DeleteExistingDestinationWithRestoreFallback(shortcutPath);
        }
    }

    public void PublishPreparedShortcut(string shortcutPath, string tempShortcutPath, ShortcutFileMetadata? metadata)
    {
        using var source = new FileStream(tempShortcutPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        SafeFileHandle? handle = null;

        try
        {
            var desiredAccess = FileSecurityNative.GENERIC_WRITE;
            if (metadata != null)
                desiredAccess |= FileSecurityNative.WRITE_DAC | FileSecurityNative.WRITE_OWNER;

            // Never COM-save directly to the scanned destination path. An attacker can fabricate a
            // link-like destination entry that resolves outside the intended shortcut tree, such as
            // a hardlink/symlink or other reparse-point trick, and then wait for elevated RunFence
            // to write through that path. The shortcut is prepared in trusted temp first, and the
            // final path is only materialized here through CREATE_NEW after any prior destination
            // entry is gone.
            handle = OpenHandle(
                shortcutPath,
                desiredAccess,
                FileSecurityNative.CREATE_NEW);
            using var destination = new FileStream(handle, FileAccess.Write);
            source.CopyTo(destination);
            destination.Flush(flushToDisk: true);

            if (metadata != null)
            {
                backupPrivilegeSecurityDescriptorAccessor.WriteOwnerAndDacl(handle, metadata.Security);
                RestoreBasicMetadata(handle, metadata);
            }
        }
        catch (Exception ex) when (IsRetryablePublishFailure(ex))
        {
            throw new ShortcutPublishRetryableException(
                $"Failed to publish prepared shortcut to {shortcutPath}.",
                ex);
        }
        finally
        {
            handle?.Dispose();
        }
    }

    private static bool IsRetryablePublishFailure(Exception ex)
    {
        if (ex is IOException or UnauthorizedAccessException)
            return true;

        if (ex is Win32Exception win32Exception)
            return win32Exception.NativeErrorCode == 5 || win32Exception.NativeErrorCode == 32;

        return false;
    }

    private static void DeleteExistingDestinationWithRestoreFallback(string shortcutPath)
    {
        using var handle = OpenHandle(
            shortcutPath,
            FileSecurityNative.DELETE,
            FileSecurityNative.OPEN_EXISTING);

        var deleteInfoEx = new FileSecurityNative.FILE_DISPOSITION_INFO_EX
        {
            Flags = FileSecurityNative.FILE_DISPOSITION_FLAGS.DELETE |
                    FileSecurityNative.FILE_DISPOSITION_FLAGS.POSIX_SEMANTICS |
                    FileSecurityNative.FILE_DISPOSITION_FLAGS.IGNORE_READONLY_ATTRIBUTE
        };
        if (!FileSecurityNative.SetFileInformationByHandle(
                handle.DangerousGetHandle(),
                FileSecurityNative.FILE_INFO_BY_HANDLE_CLASS.FileDispositionInfoEx,
                ref deleteInfoEx,
                (uint)System.Runtime.InteropServices.Marshal.SizeOf<FileSecurityNative.FILE_DISPOSITION_INFO_EX>()))
        {
            throw new Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error());
        }
    }

    private static void RestoreBasicMetadata(SafeFileHandle handle, ShortcutFileMetadata metadata)
    {
        var fileBasicInfo = new FileSecurityNative.FILE_BASIC_INFO
        {
            CreationTime = metadata.CreationTimeUtc.ToFileTimeUtc(),
            LastAccessTime = metadata.LastAccessTimeUtc.ToFileTimeUtc(),
            LastWriteTime = metadata.LastWriteTimeUtc.ToFileTimeUtc(),
            ChangeTime = metadata.LastWriteTimeUtc.ToFileTimeUtc(),
            FileAttributes = (uint)metadata.Attributes
        };
        if (!FileSecurityNative.SetFileInformationByHandle(
                handle.DangerousGetHandle(),
                FileSecurityNative.FILE_INFO_BY_HANDLE_CLASS.FileBasicInfo,
                ref fileBasicInfo,
                (uint)System.Runtime.InteropServices.Marshal.SizeOf<FileSecurityNative.FILE_BASIC_INFO>()))
        {
            throw new Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error());
        }
    }

    private static SafeFileHandle OpenHandle(string path, uint desiredAccess, uint creationDisposition)
    {
        var handle = FileSecurityNative.CreateFile(
            path,
            desiredAccess,
            FileSecurityNative.FILE_SHARE_READ | FileSecurityNative.FILE_SHARE_WRITE | FileSecurityNative.FILE_SHARE_DELETE,
            IntPtr.Zero,
            creationDisposition,
            FileSecurityNative.FILE_ATTRIBUTE_NORMAL | FileSecurityNative.FILE_FLAG_BACKUP_SEMANTICS,
            IntPtr.Zero);
        if (handle == FileSecurityNative.INVALID_HANDLE_VALUE)
            throw new Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error());

        return new SafeFileHandle(handle, ownsHandle: true);
    }
}
