using Microsoft.Win32;
using RunFence.Core;

namespace RunFence.Launch.Container;

public class AppContainerUserRegistryRoot : IAppContainerUserRegistryRoot
{
    public IRegistryKey UsersRoot => new WindowsRegistryKey(Registry.Users);
}
