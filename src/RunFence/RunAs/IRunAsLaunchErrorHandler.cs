using RunFence.Launch;

namespace RunFence.RunAs;

public interface IRunAsLaunchErrorHandler
{
    void RunWithErrorHandling(Func<LaunchExecutionResult> launchAction, string filePath);
}
