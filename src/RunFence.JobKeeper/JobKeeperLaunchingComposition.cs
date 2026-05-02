using RunFence.Launching.Resolution;

namespace RunFence.JobKeeper;

internal sealed record JobKeeperLaunchingComposition(IExecutablePathResolver ExecutablePathResolver)
{
    public static JobKeeperLaunchingComposition CreateProduction()
    {
        var fileSystem = new FileSystemExecutableFileSystem();
        var profilePathReader = new RegistryProfilePathReader();
        var pathResolver = new ExecutablePathResolver(fileSystem, profilePathReader);

        return new JobKeeperLaunchingComposition(pathResolver);
    }
}
