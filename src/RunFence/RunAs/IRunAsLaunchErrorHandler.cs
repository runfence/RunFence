namespace RunFence.RunAs;

public interface IRunAsLaunchErrorHandler
{
    void RunWithErrorHandling(Action launchAction, string filePath);
}
