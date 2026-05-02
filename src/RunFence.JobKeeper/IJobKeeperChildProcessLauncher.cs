using RunFence.Core;

namespace RunFence.JobKeeper;

public interface IJobKeeperChildProcessLauncher
{
    JobKeeperLaunchResponse Launch(JobKeeperLaunchRequest request);
}
