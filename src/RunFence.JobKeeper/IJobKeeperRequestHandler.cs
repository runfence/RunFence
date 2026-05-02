using RunFence.Core;

namespace RunFence.JobKeeper;

internal interface IJobKeeperRequestHandler
{
    JobKeeperLaunchResponse Handle(JobKeeperLaunchRequest request);
}
