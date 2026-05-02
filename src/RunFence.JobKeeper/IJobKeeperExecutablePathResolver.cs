namespace RunFence.JobKeeper;

public interface IJobKeeperExecutablePathResolver
{
    string Resolve(string exePath, IReadOnlyDictionary<string, string> environment);
}
