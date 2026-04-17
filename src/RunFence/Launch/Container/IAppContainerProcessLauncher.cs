using RunFence.Launch.Tokens;

namespace RunFence.Launch.Container;

public interface IAppContainerProcessLauncher
{
    ProcessInfo LaunchFile(ProcessLaunchTarget target, AppContainerLaunchIdentity identity);
}
