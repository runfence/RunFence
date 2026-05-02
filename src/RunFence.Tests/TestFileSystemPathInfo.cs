using System.Security.AccessControl;
using RunFence.Acl;

namespace RunFence.Tests;

public sealed class TestFileSystemPathInfo : IFileSystemPathInfo
{
    private readonly HashSet<string> _directories = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, FileSystemSecurity> _directorySecurity = new(StringComparer.OrdinalIgnoreCase);

    public TestFileSystemPathInfo AddDirectory(string path, FileSystemSecurity? security = null)
    {
        var normalized = Path.GetFullPath(path);
        _directories.Add(normalized);
        if (security != null)
            _directorySecurity[normalized] = security;
        return this;
    }

    public TestFileSystemPathInfo AddFile(string path)
    {
        _files.Add(Path.GetFullPath(path));
        return this;
    }

    public bool DirectoryExists(string path) => _directories.Contains(Path.GetFullPath(path));

    public bool FileExists(string path) => _files.Contains(Path.GetFullPath(path));

    public FileSystemSecurity GetDirectorySecurity(string path)
    {
        var normalized = Path.GetFullPath(path);
        return _directorySecurity.TryGetValue(normalized, out var security)
            ? security
            : new DirectorySecurity();
    }
}
