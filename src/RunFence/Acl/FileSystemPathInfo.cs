using System.Security.AccessControl;

namespace RunFence.Acl;

public sealed class FileSystemPathInfo : IFileSystemPathInfo
{
    public bool DirectoryExists(string path) => Directory.Exists(path);

    public bool FileExists(string path) => File.Exists(path);

    public FileSystemSecurity GetDirectorySecurity(string path) =>
        new DirectoryInfo(path).GetAccessControl();
}
