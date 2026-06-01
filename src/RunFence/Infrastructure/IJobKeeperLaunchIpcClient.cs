using RunFence.Core;

namespace RunFence.Infrastructure;

public interface IJobKeeperLaunchIpcClient
{
    Task<JobKeeperLaunchedProcess?> SendLaunchRequestAsync(string sid, bool isLow, JobKeeperLaunchRequest request, TimeSpan timeout, CancellationToken cancellationToken);
}
