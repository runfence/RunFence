namespace RunFence.RunAs;

public interface IAppEditCommitService
{
    RunAsAppEntryPersistenceResult Commit(RunAsAppEditCommitRequest request);
}
