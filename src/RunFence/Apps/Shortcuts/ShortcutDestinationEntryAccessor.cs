using System.ComponentModel;
using System.Security.AccessControl;
using Microsoft.Win32.SafeHandles;
using RunFence.Acl;
using RunFence.Infrastructure;

namespace RunFence.Apps.Shortcuts;

public sealed class ShortcutDestinationEntryAccessor(
    IShortcutDestinationNativeApi nativeApi,
    BackupPrivilegeSecurityDescriptorAccessor backupPrivilegeSecurityDescriptorAccessor) : IShortcutDestinationEntryAccessor
{
    private const uint ShareMode = FileSecurityNative.FILE_SHARE_READ |
                                   FileSecurityNative.FILE_SHARE_WRITE |
                                   FileSecurityNative.FILE_SHARE_DELETE;

    private const uint ExistingEntryFlags = FileSecurityNative.FILE_ATTRIBUTE_NORMAL |
                                            FileSecurityNative.FILE_FLAG_BACKUP_SEMANTICS |
                                            FileSecurityNative.FILE_FLAG_OPEN_REPARSE_POINT;

    private const uint ResolvedEntryFlags = FileSecurityNative.FILE_ATTRIBUTE_NORMAL |
                                            FileSecurityNative.FILE_FLAG_BACKUP_SEMANTICS;

    private const FileAttributes SafeRestoredAttributeMask =
        FileAttributes.ReadOnly |
        FileAttributes.Hidden |
        FileAttributes.System |
        FileAttributes.Archive |
        FileAttributes.NotContentIndexed;

    public ShortcutFileMetadata? TryCaptureExistingMetadata(string shortcutPath)
    {
        SafeFileHandle? handle = null;
        try
        {
            handle = nativeApi.Open(
                shortcutPath,
                FileSecurityNative.READ_CONTROL,
                ShareMode,
                FileSecurityNative.OPEN_EXISTING,
                ExistingEntryFlags);
            var fileInfo = nativeApi.GetFileInformation(handle);
            var attributes = (FileAttributes)fileInfo.dwFileAttributes;
            if ((attributes & FileAttributes.Directory) != 0)
                return null;
            if ((attributes & FileAttributes.ReparsePoint) != 0 && IsBrokenFinalNonDirectoryReparsePoint(shortcutPath))
                return null;

            var security = (FileSecurity)backupPrivilegeSecurityDescriptorAccessor.ReadOwnerAndDacl(handle, isDirectory: false);
            return new ShortcutFileMetadata(
                security,
                NormalizeRestoredAttributes(attributes),
                ToUtc(fileInfo.ftCreationTime),
                ToUtc(fileInfo.ftLastWriteTime),
                ToUtc(fileInfo.ftLastAccessTime));
        }
        catch (Win32Exception ex) when (IsMissingEntryError(ex.NativeErrorCode))
        {
            return null;
        }
        finally
        {
            handle?.Dispose();
        }
    }

    public void DeleteExistingDestination(string shortcutPath)
    {
        SafeFileHandle? handle = null;
        try
        {
            handle = nativeApi.Open(
                shortcutPath,
                FileSecurityNative.DELETE,
                ShareMode,
                FileSecurityNative.OPEN_EXISTING,
                ExistingEntryFlags);
            var fileInfo = nativeApi.GetFileInformation(handle);
            if (((FileAttributes)fileInfo.dwFileAttributes & FileAttributes.Directory) != 0)
            {
                throw new IOException(
                    $"Shortcut destination '{shortcutPath}' is a directory entry and cannot be replaced with a regular shortcut file.");
            }

            nativeApi.SetDeleteDisposition(
                handle,
                FileSecurityNative.FILE_DISPOSITION_FLAGS.DELETE |
                FileSecurityNative.FILE_DISPOSITION_FLAGS.POSIX_SEMANTICS |
                FileSecurityNative.FILE_DISPOSITION_FLAGS.IGNORE_READONLY_ATTRIBUTE);
        }
        catch (Win32Exception ex) when (IsMissingEntryError(ex.NativeErrorCode))
        {
        }
        finally
        {
            handle?.Dispose();
        }
    }

    public SafeFileHandle OpenNewDestination(string shortcutPath, uint desiredAccess)
        => nativeApi.Open(
            shortcutPath,
            desiredAccess,
            ShareMode,
            FileSecurityNative.CREATE_NEW,
            FileSecurityNative.FILE_ATTRIBUTE_NORMAL | FileSecurityNative.FILE_FLAG_BACKUP_SEMANTICS);

    private bool IsBrokenFinalNonDirectoryReparsePoint(string shortcutPath)
    {
        SafeFileHandle? resolvedHandle = null;
        try
        {
            resolvedHandle = nativeApi.Open(
                shortcutPath,
                FileSecurityNative.GENERIC_READ,
                ShareMode,
                FileSecurityNative.OPEN_EXISTING,
                ResolvedEntryFlags);
            return false;
        }
        catch (Win32Exception ex) when (IsMissingEntryError(ex.NativeErrorCode))
        {
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
        finally
        {
            resolvedHandle?.Dispose();
        }
    }

    private static bool IsMissingEntryError(int errorCode)
        => errorCode is 2 or 3;

    private static FileAttributes NormalizeRestoredAttributes(FileAttributes attributes)
        => attributes.HasFlag(FileAttributes.ReparsePoint)
            ? attributes & SafeRestoredAttributeMask
            : attributes;

    private static DateTime ToUtc(System.Runtime.InteropServices.ComTypes.FILETIME fileTime)
    {
        var high = (long)(uint)fileTime.dwHighDateTime << 32;
        var low = (uint)fileTime.dwLowDateTime;
        return DateTime.FromFileTimeUtc(high | low);
    }
}
