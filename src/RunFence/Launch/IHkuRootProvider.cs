using RunFence.Core;

namespace RunFence.Launch;

public interface IHkuRootProvider
{
    IRegistryKey OpenUsersRoot();
}
