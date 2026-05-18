namespace RunFence.JobKeeper;

public interface IJobKeeperLifetimeController
{
    void RecordRequestArrival();
    bool ShouldExit();
}
