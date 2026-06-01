using Microsoft.Win32.SafeHandles;

namespace RunFence.Infrastructure;

public interface IBackupIntentNativeFileSystem
{
    BackupIntentNativeOpenResult TryOpen(string path, bool directory);
    SafeFileHandle CreateRelativeDirectory(
        SafeFileHandle parentHandle,
        string childName,
        uint desiredAccess,
        uint shareAccess,
        byte[]? securityDescriptor = null);
    SafeFileHandle CreateRelativeFile(
        SafeFileHandle parentHandle,
        string childName,
        uint desiredAccess,
        uint shareAccess,
        bool overwrite,
        byte[]? securityDescriptor = null);

    bool TryEnumerateDirectories(SafeFileHandle handle, string rootPath, out IReadOnlyList<string> directories);

    bool TryGetLastWriteTimeUtc(SafeFileHandle handle, out DateTime lastWriteTimeUtc);
}
