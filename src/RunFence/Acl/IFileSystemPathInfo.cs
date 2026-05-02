using System.Security.AccessControl;

namespace RunFence.Acl;

public interface IFileSystemPathInfo
{
    bool DirectoryExists(string path);
    bool FileExists(string path);
    FileSystemSecurity GetDirectorySecurity(string path);
}
