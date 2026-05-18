namespace RunFence.Account.OrphanedProfiles;

public interface IProfileDirectoryRemovalService
{
    void RemoveMovedProfileDirectory(string path);
}
