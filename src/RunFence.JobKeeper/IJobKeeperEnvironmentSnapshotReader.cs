namespace RunFence.JobKeeper;

public interface IJobKeeperEnvironmentSnapshotReader
{
    Dictionary<string, string> ReadAll();
}
