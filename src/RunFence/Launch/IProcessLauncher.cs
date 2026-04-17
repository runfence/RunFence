using RunFence.Launch.Tokens;

namespace RunFence.Launch;

public interface IProcessLauncher
{
    ProcessInfo? Launch(LaunchIdentity identity, ProcessLaunchTarget target);
}
