using RunFence.Core.Models;

namespace RunFence.Launch.Container;

/// <summary>
/// Handles AppContainer profile creation and token virtualization setup.
/// </summary>
public interface IAppContainerProfileSetup
{
    AppContainerProfileSetupResult EnsureProfileUnderToken(AppContainerEntry entry, IntPtr hToken);
    void TryEnableVirtualization(IntPtr hToken);
}
