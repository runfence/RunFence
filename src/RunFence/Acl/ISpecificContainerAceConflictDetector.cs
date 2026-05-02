namespace RunFence.Acl;

public interface ISpecificContainerAceConflictDetector
{
    bool HasExplicitSpecificContainerAce(string path);
    bool HasLowIntegrityAce(string path);
}
