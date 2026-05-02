namespace RunFence.Acl;

/// <summary>
/// Path existence checks that can fall back to backup-privilege filesystem access.
/// </summary>
public interface IPathExistenceService
{
    bool PathExists(string path, out bool isFolder);
}
