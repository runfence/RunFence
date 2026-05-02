namespace RunFence.Launching.Resolution;

public sealed class FileSystemExecutableFileSystem : IExecutableFileSystem
{
    public bool FileExists(string path) => File.Exists(path);

    public FileAttributes GetAttributes(string path) => File.GetAttributes(path);
}
