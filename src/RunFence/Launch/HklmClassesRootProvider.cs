using Microsoft.Win32;
using RunFence.Core;

namespace RunFence.Launch;

public sealed class HklmClassesRootProvider : IHklmClassesRootProvider
{
    public IRegistryKey OpenClassesRoot()
        => new WindowsRegistryKey(
            RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Default)
                .OpenSubKey(@"Software\Classes")!);
}
