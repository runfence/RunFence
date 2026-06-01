using System.Security.AccessControl;

namespace RunFence.SecurityScanner;

public class FileSystemDataAccess(ShortcutResolver shortcutResolver) : IFileSystemDataAccess
{
    public bool DirectoryExists(string path) => Directory.Exists(path);
    public bool FileExists(string path) => File.Exists(path);
    public DirectorySecurity GetDirectorySecurity(string path) => new DirectoryInfo(path).GetAccessControl();
    public FileSecurity GetFileSecurity(string path) => new FileInfo(path).GetAccessControl();
    public string[] GetFilesInFolder(string folderPath) => Directory.GetFiles(folderPath);
    public ShortcutTargetInfo? ResolveShortcutTarget(string lnkPath) => shortcutResolver.ResolveShortcutTarget(lnkPath);
}
