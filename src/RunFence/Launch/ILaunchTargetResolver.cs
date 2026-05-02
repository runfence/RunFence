namespace RunFence.Launch;

public interface ILaunchTargetResolver
{
    TraversePathResult TraversePath(string path, LaunchIdentity identity);

    LaunchTargetResolutionResult ResolveFileHandler(LaunchIdentity identity, ProcessLaunchTarget target);

    LaunchTargetResolutionResult ResolveUrlHandler(LaunchIdentity identity, string url);
}
