using RunFence.Core;

namespace RunFence.Launch;

public interface IHklmClassesRootProvider
{
    IRegistryKey OpenClassesRoot();
}
