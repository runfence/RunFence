using System.Security.AccessControl;

namespace RunFence.Persistence;

public class PersistenceFileSecurityMirror : IPersistenceFileSecurityMirror
{
    public FileSecurity CaptureFileSecurity(string sourcePath)
        => new FileInfo(sourcePath).GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner);

    public void ApplyFileSecurity(string destinationPath, FileSecurity security)
        => new FileInfo(destinationPath).SetAccessControl(security);
}
