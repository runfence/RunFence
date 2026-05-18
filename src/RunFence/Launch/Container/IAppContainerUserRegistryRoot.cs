using Microsoft.Win32;

namespace RunFence.Launch.Container;

public interface IAppContainerUserRegistryRoot
{
    RegistryKey UsersRoot { get; }
}
