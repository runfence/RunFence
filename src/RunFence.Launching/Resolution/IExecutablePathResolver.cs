namespace RunFence.Launching.Resolution;

public interface IExecutablePathResolver
{
    string? TryResolvePath(string nameOrPath, ExecutablePathResolutionContext context);
}
