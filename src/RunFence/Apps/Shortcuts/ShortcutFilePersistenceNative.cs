using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using RunFence.Acl;
using RunFence.Infrastructure;

namespace RunFence.Apps.Shortcuts;

public sealed class ShortcutFilePersistenceNative(
    IShortcutDestinationNativeApi shortcutDestinationNativeApi,
    IShortcutDestinationEntryAccessor shortcutDestinationEntryAccessor,
    BackupPrivilegeSecurityDescriptorAccessor backupPrivilegeSecurityDescriptorAccessor) : IShortcutFilePersistenceNative
{
    public ShortcutFileMetadata? TryCaptureExistingMetadata(string shortcutPath) =>
        shortcutDestinationEntryAccessor.TryCaptureExistingMetadata(shortcutPath);

    public void DeleteExistingDestination(string shortcutPath) =>
        shortcutDestinationEntryAccessor.DeleteExistingDestination(shortcutPath);

    public void PublishPreparedShortcut(string shortcutPath, string tempShortcutPath, ShortcutFileMetadata? metadata)
    {
        using var source = new FileStream(tempShortcutPath, FileMode.Open, FileAccess.Read, FileShare.Read);
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
            using var handle = shortcutDestinationEntryAccessor.OpenNewDestination(shortcutPath, desiredAccess);
            using var destination = new FileStream(handle, FileAccess.Write);
            source.CopyTo(destination);
            destination.Flush(flushToDisk: true);

            if (metadata != null)
            {
                backupPrivilegeSecurityDescriptorAccessor.WriteOwnerAndDacl(handle, metadata.Security);
                RestoreBasicMetadata(handle, metadata);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            throw new ShortcutPublishFailureException(
                $"Failed to publish prepared shortcut to {shortcutPath}.",
                ex);
        }
    }

    private void RestoreBasicMetadata(SafeFileHandle handle, ShortcutFileMetadata metadata)
    {
        var fileBasicInfo = new FileSecurityNative.FILE_BASIC_INFO
        {
            CreationTime = metadata.CreationTimeUtc.ToFileTimeUtc(),
            LastAccessTime = metadata.LastAccessTimeUtc.ToFileTimeUtc(),
            LastWriteTime = metadata.LastWriteTimeUtc.ToFileTimeUtc(),
            ChangeTime = metadata.LastWriteTimeUtc.ToFileTimeUtc(),
            FileAttributes = (uint)metadata.Attributes
        };
        shortcutDestinationNativeApi.SetBasicInfo(handle, fileBasicInfo);
    }
}
