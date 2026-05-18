namespace RunFence.Infrastructure;

public interface IBackupIntentFileSystem
{
    bool FileExists(string path);

    bool DirectoryExists(string path);

    IReadOnlyList<string> EnumerateDirectories(string path);
}
