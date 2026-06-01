namespace RunFence.Infrastructure;

public sealed class BackupIntentManagedFileSystemProbe : IBackupIntentManagedFileSystemProbe
{
    public FileAttributes GetAttributes(string path) => File.GetAttributes(path);

    public IReadOnlyList<string> EnumerateDirectories(string path) => Directory.EnumerateDirectories(path).ToArray();

    public DateTime GetDirectoryLastWriteTimeUtc(string path) => Directory.GetLastWriteTimeUtc(path);
}
