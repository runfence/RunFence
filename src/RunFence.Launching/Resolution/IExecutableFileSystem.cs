namespace RunFence.Launching.Resolution;

public interface IExecutableFileSystem
{
    bool FileExists(string path);

    FileAttributes GetAttributes(string path);
}
