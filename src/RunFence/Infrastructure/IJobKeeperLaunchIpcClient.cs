using RunFence.Core;

namespace RunFence.Infrastructure;

public interface IJobKeeperLaunchIpcClient
{
    Task<int> SendLaunchRequestAsync(string sid, bool isLow, JobKeeperLaunchRequest request, TimeSpan timeout, CancellationToken cancellationToken);
}
