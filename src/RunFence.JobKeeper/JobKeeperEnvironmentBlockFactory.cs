using RunFence.Launching.Environment;

namespace RunFence.JobKeeper;

public sealed class JobKeeperEnvironmentBlockFactory : IJobKeeperEnvironmentBlockFactory
{
    public EnvironmentBlock Build(IReadOnlyDictionary<string, string> environment)
        => EnvironmentBlock.Build(environment);
}
