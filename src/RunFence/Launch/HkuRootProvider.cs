using Microsoft.Win32;
using RunFence.Core;

namespace RunFence.Launch;

public sealed class HkuRootProvider : IHkuRootProvider
{
    public IRegistryKey OpenUsersRoot()
        => new WindowsRegistryKey(RegistryKey.OpenBaseKey(RegistryHive.Users, RegistryView.Default));
}
