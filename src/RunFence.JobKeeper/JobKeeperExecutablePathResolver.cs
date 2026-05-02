using RunFence.Launching.Environment;
using RunFence.Launching.Resolution;

namespace RunFence.JobKeeper;

public sealed class JobKeeperExecutablePathResolver(IExecutablePathResolver executablePathResolver) : IJobKeeperExecutablePathResolver
{
    public string Resolve(string exePath, IReadOnlyDictionary<string, string> environment) =>
        executablePathResolver.TryResolvePath(
            exePath,
            ExecutablePathResolutionContext.TargetEnvironment(new DictionaryEnvironmentVariableReader(environment))) ?? exePath;
}
