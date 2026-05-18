using Microsoft.Win32;

namespace RunFence.Launch.Container;

public class AppContainerComRegistryRoots : IAppContainerComRegistryRoots
{
    public RegistryKey AppIdRoot => Registry.ClassesRoot;
    public RegistryKey MachineRoot => Registry.LocalMachine;
}
