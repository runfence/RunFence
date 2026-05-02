using RunFence.Core;

namespace RunFence.JobKeeper;

internal sealed class JobKeeperRequestHandler(IJobKeeperChildProcessLauncher childProcessLauncher) : IJobKeeperRequestHandler
{
    private const int ErrorInvalidParameter = 87;
    private const int ErrorUnhandledException = 31;

    public JobKeeperLaunchResponse Handle(JobKeeperLaunchRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ExePath))
            return new JobKeeperLaunchResponse(0, ErrorInvalidParameter);

        try
        {
            return childProcessLauncher.Launch(request);
        }
        catch
        {
            return new JobKeeperLaunchResponse(0, ErrorUnhandledException);
        }
    }
}
