using System.Security.AccessControl;

namespace RunFence.Tests.TestHelpers;

public sealed class AclIntegrationCleanupScope : IDisposable
{
    private readonly List<string> _tempPaths = [];
    private readonly List<(string Path, string Sddl)> _originalAcls = [];

    public void TrackTempPath(string path)
    {
        if (!_tempPaths.Contains(path, StringComparer.OrdinalIgnoreCase))
            _tempPaths.Add(path);
    }

    public void TrackOriginalAcl(string path, string sddl)
    {
        if (_originalAcls.Any(entry => string.Equals(entry.Path, path, StringComparison.OrdinalIgnoreCase)))
            return;

        _originalAcls.Add((path, sddl));
    }

    public void Dispose()
    {
        foreach (var (path, sddl) in _originalAcls.AsEnumerable().Reverse())
            RestoreAcl(path, sddl);

        foreach (var path in _tempPaths.AsEnumerable().Reverse())
            DeletePath(path);
    }

    private static void RestoreAcl(string path, string sddl)
    {
        if (Directory.Exists(path))
        {
            var security = new DirectorySecurity();
            security.SetSecurityDescriptorSddlForm(sddl);
            new DirectoryInfo(path).SetAccessControl(security);
            return;
        }

        if (File.Exists(path))
        {
            var security = new FileSecurity();
            security.SetSecurityDescriptorSddlForm(sddl);
            new FileInfo(path).SetAccessControl(security);
        }
    }

    private static void DeletePath(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
            return;
        }

        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }
}
