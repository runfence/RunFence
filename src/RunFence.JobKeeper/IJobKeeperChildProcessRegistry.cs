namespace RunFence.JobKeeper;

public interface IJobKeeperChildProcessRegistry
{
    void Register(IntPtr processHandle);
    int PruneExitedAndCountActive();
    bool TryExitAfterCleaningIgnoredProcesses();
}
