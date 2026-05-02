namespace RunFence.Launch.Tokens;

public interface IRestrictedJobLaunchCoordinatorInitializer
{
    void Initialize(IRestrictedJobProcessLauncher processLauncher);
}
