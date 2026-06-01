using System.Security.AccessControl;
using System.Security.Principal;

namespace RunFence.Tests;

public sealed class TemporaryDenyAcl : IDisposable
{
    private readonly string _directoryPath;
    private readonly DirectorySecurity _originalSecurity;
    private bool _disposed;

    private TemporaryDenyAcl(string directoryPath, SecurityIdentifier sid, FileSystemRights rights)
    {
        _directoryPath = directoryPath;

        var directory = new DirectoryInfo(directoryPath);
        _originalSecurity = directory.GetAccessControl(AccessControlSections.Access);

        ApplyDeny(directory, sid, rights);
    }

    private static void ApplyDeny(DirectoryInfo directory, SecurityIdentifier sid, FileSystemRights rights)
    {
        var security = directory.GetAccessControl(AccessControlSections.Access);
        security.RemoveAccessRuleAll(new FileSystemAccessRule(
            sid,
            rights,
            AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(
            sid,
            rights,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Deny));
        directory.SetAccessControl(security);
    }

    public static TemporaryDenyAcl Apply(string directoryPath, SecurityIdentifier sid, FileSystemRights rights)
        => new(directoryPath, sid, rights);

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        var directory = new DirectoryInfo(_directoryPath);
        directory.SetAccessControl(_originalSecurity);
    }
}
