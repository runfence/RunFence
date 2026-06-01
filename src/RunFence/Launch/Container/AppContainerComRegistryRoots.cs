using Microsoft.Win32;
using RunFence.Core;

namespace RunFence.Launch.Container;

public class AppContainerComRegistryRoots : IAppContainerComRegistryRoots
{
    public IRegistryKey AppIdRoot => new WindowsRegistryKey(Registry.ClassesRoot);
    public IRegistryKey MachineRoot => new WindowsRegistryKey(Registry.LocalMachine);
}
