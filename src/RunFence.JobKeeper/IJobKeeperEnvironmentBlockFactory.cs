using RunFence.Launching.Environment;

namespace RunFence.JobKeeper;

public interface IJobKeeperEnvironmentBlockFactory
{
    EnvironmentBlock Build(IReadOnlyDictionary<string, string> environment);
}
