using Microsoft.Win32;

namespace RunFence.Launch.Container;

public interface IAppContainerComRegistryRoots
{
    RegistryKey AppIdRoot { get; }
    RegistryKey MachineRoot { get; }
}
