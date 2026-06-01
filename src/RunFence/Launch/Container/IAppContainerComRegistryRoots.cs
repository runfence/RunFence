using RunFence.Core;

namespace RunFence.Launch.Container;

public interface IAppContainerComRegistryRoots
{
    IRegistryKey AppIdRoot { get; }
    IRegistryKey MachineRoot { get; }
}
