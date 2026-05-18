using System.Security.AccessControl;

namespace RunFence.Persistence;

public interface IPersistenceFileSecurityMirror
{
    FileSecurity CaptureFileSecurity(string sourcePath);
    void ApplyFileSecurity(string destinationPath, FileSecurity security);
}
