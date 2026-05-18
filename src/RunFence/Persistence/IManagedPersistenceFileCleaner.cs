namespace RunFence.Persistence;

public interface IManagedPersistenceFileCleaner
{
    void DeletePrimaryAndManagedArtifacts(string primaryFilePath);
}
