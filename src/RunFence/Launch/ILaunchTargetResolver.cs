using RunFence.Core.Models;

namespace RunFence.Launch;

public interface ILaunchTargetResolver
{
    TraversePathResult TraversePath(string path, LaunchIdentity identity);
    TraversePathResult TraversePath(string path, LaunchIdentity identity, AppDatabase databaseSnapshot);

    LaunchTargetResolutionResult ResolveFileHandler(LaunchIdentity identity, ProcessLaunchTarget target, string? extension = null);
    LaunchTargetResolutionResult ResolveFileHandler(
        LaunchIdentity identity,
        ProcessLaunchTarget target,
        AppDatabase databaseSnapshot,
        string? extension = null);

    LaunchTargetResolutionResult ResolveUrlHandler(LaunchIdentity identity, string url);
    LaunchTargetResolutionResult ResolveUrlHandler(LaunchIdentity identity, string url, AppDatabase databaseSnapshot);
}
