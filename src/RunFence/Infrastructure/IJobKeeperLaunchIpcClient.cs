using RunFence.Core;

namespace RunFence.Infrastructure;

public interface IJobKeeperLaunchIpcClient
{
    int SendLaunchRequest(string sid, bool isLow, JobKeeperLaunchRequest request);
}
