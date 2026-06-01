using System.Security.AccessControl;
using RunFence.Core.Models;

namespace RunFence.Acl;

public sealed class GrantIntentRestoreSnapshot
{
    public GrantIntentRestoreSnapshot(
        GrantedPathEntry? runtimeEntry,
        IReadOnlyList<GrantIntentRestoreLocation> locations,
        FileSystemSecurity? previousTargetSecurity = null,
        IReadOnlyList<string>? touchedTraversePaths = null)
    {
        RuntimeEntry = runtimeEntry?.Clone();
        Locations = locations
            .Select(location => new GrantIntentRestoreLocation(location.StoreIdentity, location.Entry))
            .ToList();
        PreviousTargetSecurity = previousTargetSecurity == null
            ? null
            : CloneSecurity(previousTargetSecurity);
        TouchedTraversePaths = touchedTraversePaths?.ToList();
    }

    public GrantedPathEntry? RuntimeEntry { get; }

    public IReadOnlyList<GrantIntentRestoreLocation> Locations { get; }

    public FileSystemSecurity? PreviousTargetSecurity { get; }

    public IReadOnlyList<string>? TouchedTraversePaths { get; }

    private static FileSystemSecurity CloneSecurity(FileSystemSecurity security)
    {
        return security switch
        {
            DirectorySecurity directorySecurity => CloneDirectorySecurity(directorySecurity),
            FileSecurity fileSecurity => CloneFileSecurity(fileSecurity),
            _ => throw new InvalidOperationException(
                $"Unsupported filesystem security type '{security.GetType().FullName}'.")
        };
    }

    private static DirectorySecurity CloneDirectorySecurity(DirectorySecurity security)
    {
        var clone = new DirectorySecurity();
        clone.SetSecurityDescriptorBinaryForm(security.GetSecurityDescriptorBinaryForm());
        return clone;
    }

    private static FileSecurity CloneFileSecurity(FileSecurity security)
    {
        var clone = new FileSecurity();
        clone.SetSecurityDescriptorBinaryForm(security.GetSecurityDescriptorBinaryForm());
        return clone;
    }
}
