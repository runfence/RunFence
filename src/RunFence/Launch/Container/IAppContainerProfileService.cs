using RunFence.Core.Models;

namespace RunFence.Launch.Container;

/// <summary>
/// Profile lifecycle and identity operations for AppContainers.
/// </summary>
public interface IAppContainerProfileService
{
    /// <summary>Creates the OS AppContainer profile and data folder.</summary>
    AppContainerProfileSetupResult CreateProfile(AppContainerEntry entry);

    /// <summary>Deletes the OS profile and data folder. Disables loopback first if needed.</summary>
    Task DeleteProfile(string name, bool hadLoopback = false);

    /// <summary>Ensures the AppContainer profile exists (creates if missing).</summary>
    AppContainerProfileSetupResult EnsureProfile(AppContainerEntry entry);

    /// <summary>Returns the AppContainer SID string derived from the profile name.</summary>
    string GetSid(string name);
}
