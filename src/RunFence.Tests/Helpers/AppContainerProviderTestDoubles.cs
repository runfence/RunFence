using Microsoft.Win32;
using RunFence.Launch.Container;

namespace RunFence.Tests.Helpers;

public static class AppContainerProviderTestDoubles
{
    public static IAppContainerComRegistryRoots CreateComRegistryRoots(RegistryKey appIdRoot, RegistryKey machineRoot)
        => new FixedComRegistryRoots(appIdRoot, machineRoot);

    public static IAppContainerUserRegistryRoot CreateUserRegistryRoot(RegistryKey usersRoot)
        => new FixedUserRegistryRoot(usersRoot);

    public static IAppContainerPathProvider CreatePathProvider(string containersRootPath)
        => new FixedPathProvider(containersRootPath);

    private sealed class FixedComRegistryRoots(RegistryKey appIdRoot, RegistryKey machineRoot)
        : IAppContainerComRegistryRoots
    {
        public RegistryKey AppIdRoot { get; } = appIdRoot;
        public RegistryKey MachineRoot { get; } = machineRoot;
    }

    private sealed class FixedUserRegistryRoot(RegistryKey usersRoot) : IAppContainerUserRegistryRoot
    {
        public RegistryKey UsersRoot { get; } = usersRoot;
    }

    private sealed class FixedPathProvider(string containersRootPath) : IAppContainerPathProvider
    {
        public string GetContainersRootPath()
            => containersRootPath;

        public string GetContainerDataPath(string profileName)
            => Path.Combine(containersRootPath, profileName);
    }
}
