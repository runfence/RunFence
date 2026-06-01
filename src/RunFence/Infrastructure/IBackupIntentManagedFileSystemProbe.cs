namespace RunFence.Infrastructure;

public interface IBackupIntentManagedFileSystemProbe
{
    FileAttributes GetAttributes(string path);

    IReadOnlyList<string> EnumerateDirectories(string path);

    DateTime GetDirectoryLastWriteTimeUtc(string path);
}
