using Microsoft.Win32;

namespace RunFence.Launch.Container;

public class AppContainerUserRegistryRoot : IAppContainerUserRegistryRoot
{
    public RegistryKey UsersRoot => Registry.Users;
}
