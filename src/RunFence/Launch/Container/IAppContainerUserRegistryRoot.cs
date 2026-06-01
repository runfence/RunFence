using RunFence.Core;

namespace RunFence.Launch.Container;

public interface IAppContainerUserRegistryRoot
{
    IRegistryKey UsersRoot { get; }
}
