namespace RunFence.Acl;

/// <summary>
/// Path existence checks that use the ACL accessor's backup-privilege fallback.
/// </summary>
public class PathExistenceService(IAclAccessor acl) : IPathExistenceService
{
    public bool PathExists(string path, out bool isFolder)
        => acl.PathExists(path, out isFolder);
}
