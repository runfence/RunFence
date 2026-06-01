namespace RunFence.Infrastructure;

public enum BackupIntentPathState
{
    Exists,
    Missing,
    Unknown
}

public interface IBackupIntentFileSystem
{
    BackupIntentPathState GetFileState(string path);

    BackupIntentPathState GetDirectoryState(string path);

    bool TryEnumerateDirectories(string path, out IReadOnlyList<string> directories);

    bool TryGetDirectoryLastWriteTimeUtc(string path, out DateTime lastWriteTimeUtc);
}
