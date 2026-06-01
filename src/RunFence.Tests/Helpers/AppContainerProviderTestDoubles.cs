using RunFence.Core;
using RunFence.Launch.Container;

namespace RunFence.Tests.Helpers;

public static class AppContainerProviderTestDoubles
{
    public static IAppContainerComRegistryRoots CreateComRegistryRoots(IRegistryKey appIdRoot, IRegistryKey machineRoot)
        => new FixedComRegistryRoots(appIdRoot, machineRoot);

    public static IAppContainerUserRegistryRoot CreateUserRegistryRoot(IRegistryKey usersRoot)
        => new FixedUserRegistryRoot(usersRoot);

    public static IAppContainerPathProvider CreatePathProvider(string containersRootPath)
        => new FixedPathProvider(containersRootPath);

    private sealed class FixedComRegistryRoots(IRegistryKey appIdRoot, IRegistryKey machineRoot)
        : IAppContainerComRegistryRoots
    {
        public IRegistryKey AppIdRoot { get; } = appIdRoot;
        public IRegistryKey MachineRoot { get; } = machineRoot;
    }

    private sealed class FixedUserRegistryRoot(IRegistryKey usersRoot) : IAppContainerUserRegistryRoot
    {
        public IRegistryKey UsersRoot { get; } = usersRoot;
    }

    private sealed class FixedPathProvider(string containersRootPath) : IAppContainerPathProvider
    {
        public string GetContainersRootPath()
            => containersRootPath;

        public string GetContainerDataPath(string profileName)
            => Path.Combine(containersRootPath, profileName);
    }
}
